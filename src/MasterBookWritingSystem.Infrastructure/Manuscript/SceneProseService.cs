using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Manuscript;

public sealed class SceneProseService : ISceneProseService
{
    private readonly IProjectService _projects;
    private readonly IChapterFileStore _files;
    private readonly IRecoveryJournalService _journal;
    private readonly ISnapshotService _snapshots;

    public SceneProseService(
        IProjectService projects,
        IChapterFileStore files,
        IRecoveryJournalService journal,
        ISnapshotService snapshots)
    {
        _projects = projects;
        _files = files;
        _journal = journal;
        _snapshots = snapshots;
    }

    public async Task SyncAssignmentFilesAsync(
        Guid projectId,
        Guid sceneId,
        Guid? fromChapterId,
        Guid? toChapterId,
        CancellationToken cancellationToken = default)
    {
        if (fromChapterId == toChapterId)
        {
            return;
        }

        var root = RequireRoot(projectId);
        var touched = new List<Guid>();
        if (fromChapterId is { } from)
        {
            touched.Add(from);
        }

        if (toChapterId is { } to)
        {
            touched.Add(to);
        }

        if (touched.Count == 0)
        {
            return;
        }

        await _snapshots
            .CreateSafetySnapshotAsync(projectId, SafetySnapshotReason.StructuralHierarchyEdit, cancellationToken)
            .ConfigureAwait(false);

        foreach (var chapterId in touched.Distinct())
        {
            var chapter = await LoadChapterAsync(projectId, chapterId, cancellationToken).ConfigureAwait(false);
            string markdown;
            try
            {
                markdown = await _files.LoadAsync(root, chapter, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                markdown = $"# {chapter.Title}\n\n";
            }

            var entryId = RecoveryJournalIds.Create(
                projectId,
                RecoveryEntityType.ManuscriptChapter,
                chapterId);
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

            var next = markdown;
            if (fromChapterId == chapterId)
            {
                next = SceneProseAssociation.UnwrapScene(next, sceneId);
            }

            if (toChapterId == chapterId)
            {
                next = SceneProseAssociation.AppendEmptyRegion(next, sceneId);
            }

            if (!string.Equals(next, markdown, StringComparison.Ordinal))
            {
                await _files.SaveAsync(root, chapter, next, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task UnwrapSceneFileAsync(
        Guid projectId,
        Guid chapterId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        var root = RequireRoot(projectId);
        var chapter = await LoadChapterAsync(projectId, chapterId, cancellationToken).ConfigureAwait(false);
        string markdown;
        try
        {
            markdown = await _files.LoadAsync(root, chapter, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return;
        }

        var next = SceneProseAssociation.UnwrapScene(markdown, sceneId);
        if (string.Equals(next, markdown, StringComparison.Ordinal))
        {
            return;
        }

        var entryId = RecoveryJournalIds.Create(
            projectId,
            RecoveryEntityType.ManuscriptChapter,
            chapterId);
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

        await _files.SaveAsync(root, chapter, next, cancellationToken).ConfigureAwait(false);
        await _journal.ClearAsync(projectId, entryId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearChapterJournalsAsync(
        Guid projectId,
        IEnumerable<Guid> chapterIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var chapterId in chapterIds.Distinct())
        {
            var entryId = RecoveryJournalIds.Create(
                projectId,
                RecoveryEntityType.ManuscriptChapter,
                chapterId);
            await _journal.ClearAsync(projectId, entryId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Chapter> LoadChapterAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        await using var context = Open(projectId);
        var record = await context.Chapters
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == chapterId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Chapter '{chapterId}' was not found.");
        SqliteConnection.ClearAllPools();
        return new Chapter
        {
            Id = record.Id,
            ProjectId = record.ProjectId,
            PartId = record.PartId,
            SequenceNumber = record.SequenceNumber,
            Title = record.Title,
            RelativeMarkdownPath = record.RelativeMarkdownPath,
            WordCount = record.WordCount,
        };
    }

    private ProjectDbContext Open(Guid projectId)
    {
        var root = RequireRoot(projectId);
        return ProjectDbContextFactory.Create(Path.Combine(root, ProjectPaths.DatabaseFileName));
    }

    private string RequireRoot(Guid projectId)
    {
        var project = _projects.ActiveProject
            ?? throw new InvalidOperationException("Open a project before editing scene prose associations.");
        if (project.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return project.RootPath;
    }
}
