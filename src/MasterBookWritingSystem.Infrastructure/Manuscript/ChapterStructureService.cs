using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Manuscript;

public sealed class ChapterStructureService : IChapterStructureService
{
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly ISnapshotService _snapshots;
    private readonly IRecoveryJournalService _journal;
    private readonly IStoryChangeNotifier _changes;

    public ChapterStructureService(
        IProjectService projects,
        IChapterService chapters,
        ISnapshotService snapshots,
        IRecoveryJournalService journal,
        IStoryChangeNotifier changes)
    {
        _projects = projects;
        _chapters = chapters;
        _snapshots = snapshots;
        _journal = journal;
        _changes = changes;
    }

    public async Task<ChapterSplitResult> SplitChapterAtAsync(
        Guid projectId,
        Guid chapterId,
        int splitIndex,
        string? rightChapterTitle = null,
        CancellationToken cancellationToken = default)
    {
        var leftChapter = await _chapters.GetAsync(projectId, chapterId, cancellationToken).ConfigureAwait(false);
        var markdown = await _chapters.LoadContentAsync(projectId, chapterId, cancellationToken)
            .ConfigureAwait(false);

        // Reject before any mutation.
        ChapterStructureRules.EnsureValidSplitIndex(markdown, splitIndex);
        if (splitIndex == 0 || splitIndex == markdown.Length)
        {
            throw new InvalidOperationException("Split position must leave content on both sides.");
        }

        var (leftText, rightText) = ChapterStructureRules.SplitMarkdown(markdown, splitIndex);
        if (string.IsNullOrWhiteSpace(leftText) || string.IsNullOrWhiteSpace(rightText))
        {
            throw new InvalidOperationException("Split would create an empty chapter.");
        }

        var doc = SceneProseAssociation.Parse(markdown);
        var scenesMoving = doc.Spans
            .Where(span => span.MarkerStartIndex >= splitIndex)
            .Select(span => span.SceneId)
            .Distinct()
            .ToList();

        await _snapshots
            .CreateSafetySnapshotAsync(projectId, SafetySnapshotReason.StructuralHierarchyEdit, cancellationToken)
            .ConfigureAwait(false);

        var entryId = RecoveryJournalIds.Create(projectId, RecoveryEntityType.ManuscriptChapter, chapterId);
        await _journal.UpsertAsync(
                new RecoveryJournalEntry
                {
                    EntryId = entryId,
                    ProjectId = projectId,
                    EntityType = RecoveryEntityType.ManuscriptChapter,
                    EntityId = chapterId,
                    DraftText = markdown,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var title = string.IsNullOrWhiteSpace(rightChapterTitle)
            ? $"{leftChapter.Title} (continued)"
            : rightChapterTitle.Trim();

        Chapter? rightChapter = null;
        var leftWritten = false;
        try
        {
            // Files first: persist left, create right chapter file, then metadata moves.
            await _chapters.SaveContentAsync(projectId, chapterId, leftText, cancellationToken)
                .ConfigureAwait(false);
            leftWritten = true;
            rightChapter = await _chapters.CreateAsync(projectId, title, cancellationToken)
                .ConfigureAwait(false);
            await _chapters.SaveContentAsync(projectId, rightChapter.Id, rightText, cancellationToken)
                .ConfigureAwait(false);

            await InsertChapterAfterAsync(projectId, leftChapter.Id, rightChapter.Id, cancellationToken)
                .ConfigureAwait(false);

            if (leftChapter.PartId != rightChapter.PartId)
            {
                // CreateAsync may assign default part; keep split siblings under the same part.
                await using var context = Open(projectId);
                var right = await context.Chapters
                    .FirstAsync(item => item.Id == rightChapter.Id, cancellationToken)
                    .ConfigureAwait(false);
                right.PartId = leftChapter.PartId;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                SqliteConnection.ClearAllPools();
            }

            await MoveScenesToChapterAsync(projectId, scenesMoving, rightChapter.Id, cancellationToken)
                .ConfigureAwait(false);

            await _journal.ClearAsync(projectId, entryId, cancellationToken).ConfigureAwait(false);
            _changes.Publish(new StoryChangeEventArgs
            {
                ProjectId = projectId,
                Kind = StoryChangeKind.HierarchyChanged,
                EntityId = chapterId,
            });

            return new ChapterSplitResult
            {
                LeftChapter = await _chapters.GetAsync(projectId, chapterId, cancellationToken).ConfigureAwait(false),
                RightChapter = await _chapters.GetAsync(projectId, rightChapter.Id, cancellationToken)
                    .ConfigureAwait(false),
            };
        }
        catch
        {
            await CompensateSplitAsync(projectId, chapterId, markdown, leftWritten, rightChapter)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ChapterMergeResult> MergeAdjacentChaptersAsync(
        Guid projectId,
        Guid leftChapterId,
        Guid rightChapterId,
        CancellationToken cancellationToken = default)
    {
        var left = await _chapters.GetAsync(projectId, leftChapterId, cancellationToken).ConfigureAwait(false);
        var right = await _chapters.GetAsync(projectId, rightChapterId, cancellationToken).ConfigureAwait(false);
        if (right.SequenceNumber != left.SequenceNumber + 1)
        {
            throw new InvalidOperationException("Only adjacent chapters (by sequence) can be merged.");
        }

        var leftText = await _chapters.LoadContentAsync(projectId, leftChapterId, cancellationToken)
            .ConfigureAwait(false);
        var rightText = await _chapters.LoadContentAsync(projectId, rightChapterId, cancellationToken)
            .ConfigureAwait(false);

        // Reject overlapping scene ids before mutation.
        var leftSpans = SceneProseAssociation.Parse(leftText).Spans.Select(span => span.SceneId).ToHashSet();
        var rightSpans = SceneProseAssociation.Parse(rightText).Spans.Select(span => span.SceneId).ToHashSet();
        if (leftSpans.Overlaps(rightSpans))
        {
            throw new InvalidOperationException(
                "Cannot merge chapters that both contain prose regions for the same scene id.");
        }

        var merged = ChapterStructureRules.MergeMarkdown(leftText, rightText);

        await _snapshots
            .CreateSafetySnapshotAsync(projectId, SafetySnapshotReason.StructuralHierarchyEdit, cancellationToken)
            .ConfigureAwait(false);

        var leftJournal = RecoveryJournalIds.Create(projectId, RecoveryEntityType.ManuscriptChapter, leftChapterId);
        var rightJournal = RecoveryJournalIds.Create(projectId, RecoveryEntityType.ManuscriptChapter, rightChapterId);
        await _journal.UpsertAsync(
                new RecoveryJournalEntry
                {
                    EntryId = leftJournal,
                    ProjectId = projectId,
                    EntityType = RecoveryEntityType.ManuscriptChapter,
                    EntityId = leftChapterId,
                    DraftText = leftText,
                },
                cancellationToken)
            .ConfigureAwait(false);
        await _journal.UpsertAsync(
                new RecoveryJournalEntry
                {
                    EntryId = rightJournal,
                    ProjectId = projectId,
                    EntityType = RecoveryEntityType.ManuscriptChapter,
                    EntityId = rightChapterId,
                    DraftText = rightText,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var leftMerged = false;
        var rightRemoved = false;
        try
        {
            await _chapters.SaveContentAsync(projectId, leftChapterId, merged, cancellationToken)
                .ConfigureAwait(false);
            leftMerged = true;

            await using (var context = Open(projectId))
            {
                var rightScenes = await context.Scenes
                    .Where(scene => scene.ProjectId == projectId && scene.ChapterId == rightChapterId)
                    .OrderBy(scene => scene.SequenceNumber)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var next = await context.Scenes
                    .Where(scene => scene.ProjectId == projectId && scene.ChapterId == leftChapterId)
                    .Select(scene => (int?)scene.SequenceNumber)
                    .MaxAsync(cancellationToken)
                    .ConfigureAwait(false) ?? 0;
                foreach (var scene in rightScenes)
                {
                    next++;
                    scene.ChapterId = leftChapterId;
                    scene.SequenceNumber = next;
                    scene.LastEditedUtc = DateTimeOffset.UtcNow;
                }

                var rightRecord = await context.Chapters
                    .FirstAsync(item => item.Id == rightChapterId, cancellationToken)
                    .ConfigureAwait(false);
                context.Chapters.Remove(rightRecord);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                SqliteConnection.ClearAllPools();
            }

            rightRemoved = true;
            var root = RequireRoot(projectId);
            var rightPath = Path.Combine(
                root,
                right.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(rightPath))
            {
                File.Delete(rightPath);
            }

            await RenumberChaptersAsync(projectId, cancellationToken).ConfigureAwait(false);
            await _journal.ClearAsync(projectId, leftJournal, cancellationToken).ConfigureAwait(false);
            await _journal.ClearAsync(projectId, rightJournal, cancellationToken).ConfigureAwait(false);
            _changes.Publish(new StoryChangeEventArgs
            {
                ProjectId = projectId,
                Kind = StoryChangeKind.HierarchyChanged,
                EntityId = leftChapterId,
            });

            return new ChapterMergeResult
            {
                SurvivingChapter = await _chapters.GetAsync(projectId, leftChapterId, cancellationToken)
                    .ConfigureAwait(false),
                RemovedChapterId = rightChapterId,
            };
        }
        catch
        {
            await CompensateMergeAsync(
                    projectId,
                    leftChapterId,
                    leftText,
                    leftMerged,
                    right,
                    rightText,
                    rightRemoved)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task CompensateSplitAsync(
        Guid projectId,
        Guid leftChapterId,
        string originalMarkdown,
        bool leftWritten,
        Chapter? rightChapter)
    {
        try
        {
            if (rightChapter is not null)
            {
                await using (var context = Open(projectId))
                {
                    var orphaned = await context.Scenes
                        .Where(scene => scene.ProjectId == projectId && scene.ChapterId == rightChapter.Id)
                        .ToListAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    foreach (var scene in orphaned)
                    {
                        scene.ChapterId = leftChapterId;
                    }

                    await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                    SqliteConnection.ClearAllPools();
                }

                await _chapters.DeleteAsync(projectId, rightChapter.Id, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // Journal retains pre-split prose for Recovery Centre.
        }

        if (!leftWritten)
        {
            return;
        }

        try
        {
            await _chapters.SaveContentAsync(projectId, leftChapterId, originalMarkdown, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // Journal retains pre-split prose for Recovery Centre.
        }
    }

    private async Task CompensateMergeAsync(
        Guid projectId,
        Guid leftChapterId,
        string leftText,
        bool leftMerged,
        Chapter right,
        string rightText,
        bool rightRemoved)
    {
        try
        {
            if (leftMerged)
            {
                await _chapters.SaveContentAsync(projectId, leftChapterId, leftText, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // Journals retain evidence.
        }

        if (!rightRemoved)
        {
            try
            {
                await _chapters.SaveContentAsync(projectId, right.Id, rightText, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Journals retain evidence.
            }

            return;
        }

        // Right chapter row was removed — recreate metadata + file and move marker scenes back.
        try
        {
            await using var context = Open(projectId);
            var exists = await context.Chapters.AnyAsync(item => item.Id == right.Id, CancellationToken.None)
                .ConfigureAwait(false);
            if (!exists)
            {
                context.Chapters.Add(new Persistence.Entities.ChapterRecord
                {
                    Id = right.Id,
                    ProjectId = projectId,
                    Title = right.Title,
                    SequenceNumber = right.SequenceNumber,
                    RelativeMarkdownPath = right.RelativeMarkdownPath,
                    PartId = right.PartId,
                    WordCount = right.WordCount,
                });
                await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }

            SqliteConnection.ClearAllPools();
            await _chapters.SaveContentAsync(projectId, right.Id, rightText, CancellationToken.None)
                .ConfigureAwait(false);

            var sceneIds = SceneProseAssociation.Parse(rightText).Spans
                .Select(span => span.SceneId)
                .Distinct()
                .ToList();
            if (sceneIds.Count > 0)
            {
                await MoveScenesToChapterAsync(projectId, sceneIds, right.Id, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            await RenumberChaptersAsync(projectId, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Journals + safety snapshot retain evidence.
        }
    }

    private async Task InsertChapterAfterAsync(
        Guid projectId,
        Guid afterChapterId,
        Guid movingChapterId,
        CancellationToken cancellationToken)
    {
        await using var context = Open(projectId);
        var chapters = await context.Chapters
            .Where(item => item.ProjectId == projectId)
            .OrderBy(item => item.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var after = chapters.FirstOrDefault(item => item.Id == afterChapterId)
            ?? throw new InvalidOperationException("Anchor chapter was not found.");
        var moving = chapters.FirstOrDefault(item => item.Id == movingChapterId)
            ?? throw new InvalidOperationException("Split chapter was not found.");

        var desired = after.SequenceNumber + 1;
        if (moving.SequenceNumber == desired)
        {
            SqliteConnection.ClearAllPools();
            return;
        }

        // Park mover, shift others, then place.
        moving.SequenceNumber = int.MinValue;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var chapter in chapters
                     .Where(item => item.Id != movingChapterId && item.SequenceNumber >= desired)
                     .OrderByDescending(item => item.SequenceNumber))
        {
            chapter.SequenceNumber += 1;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        moving.SequenceNumber = desired;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    private async Task MoveScenesToChapterAsync(
        Guid projectId,
        IReadOnlyList<Guid> sceneIds,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        if (sceneIds.Count == 0)
        {
            return;
        }

        await using var context = Open(projectId);
        var scenes = await context.Scenes
            .Where(scene => scene.ProjectId == projectId && sceneIds.Contains(scene.Id))
            .OrderBy(scene => scene.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var next = await context.Scenes
            .Where(scene => scene.ProjectId == projectId && scene.ChapterId == chapterId)
            .Select(scene => (int?)scene.SequenceNumber)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;
        foreach (var scene in scenes)
        {
            next++;
            scene.ChapterId = chapterId;
            scene.SequenceNumber = next;
            scene.LastEditedUtc = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    private async Task RenumberChaptersAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var context = Open(projectId);
        var chapters = await context.Chapters
            .Where(item => item.ProjectId == projectId)
            .OrderBy(item => item.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        for (var i = 0; i < chapters.Count; i++)
        {
            chapters[i].SequenceNumber = i + 1;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    private ProjectDbContext Open(Guid projectId)
    {
        var root = RequireRoot(projectId);
        return ProjectDbContextFactory.Create(Path.Combine(root, ProjectPaths.DatabaseFileName));
    }

    private string RequireRoot(Guid projectId)
    {
        var project = _projects.ActiveProject
            ?? throw new InvalidOperationException("Open a project before structural chapter edits.");
        if (project.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return project.RootPath;
    }
}
