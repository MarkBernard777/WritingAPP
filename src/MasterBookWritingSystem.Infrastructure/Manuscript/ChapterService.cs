using System.Text;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.IO;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Manuscript;

public sealed class ChapterService : IChapterService
{
    private readonly IProjectService _projectService;
    private readonly IChapterFileStore _chapterFileStore;
    private readonly ISnapshotService _snapshots;
    private readonly IRecoveryJournalService _journal;

    public ChapterService(
        IProjectService projectService,
        IChapterFileStore chapterFileStore,
        ISnapshotService snapshots,
        IRecoveryJournalService journal)
    {
        _projectService = projectService;
        _chapterFileStore = chapterFileStore;
        _snapshots = snapshots;
        _journal = journal;
    }

    public async Task<IReadOnlyList<Chapter>> GetAllAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var records = await context.Chapters
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .OrderBy(item => item.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<Chapter> GetAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken = default)
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
        return ToDomain(record);
    }

    public async Task<Chapter> CreateAsync(
        Guid projectId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var rootPath = RequireActiveRoot(projectId);

        int nextSequence;
        await using (var context = Open(projectId))
        {
            nextSequence = await context.Chapters
                .Where(item => item.ProjectId == projectId)
                .Select(item => (int?)item.SequenceNumber)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false) ?? 0;
        }

        SqliteConnection.ClearAllPools();
        nextSequence++;

        var chapter = new Chapter
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            SequenceNumber = nextSequence,
            Title = title.Trim(),
            RelativeMarkdownPath = BuildRelativePath(nextSequence, title),
        };

        var starter = $"# {chapter.Title}\n\n";
        await _chapterFileStore.SaveAsync(rootPath, chapter, starter, cancellationToken)
            .ConfigureAwait(false);

        return await GetAsync(projectId, chapter.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Chapter> RenameAsync(
        Guid projectId,
        Guid chapterId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var rootPath = RequireActiveRoot(projectId);

        await using var context = Open(projectId);
        var record = await context.Chapters
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == chapterId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Chapter '{chapterId}' was not found.");

        record.Title = title.Trim();
        var project = await context.Projects.FirstOrDefaultAsync(item => item.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is not null)
        {
            project.LastEditedUtc = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();

        // Keep the markdown filename stable; only metadata title changes.
        _ = rootPath;
        return ToDomain(record);
    }

    public async Task DeleteAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);

        await using var context = Open(projectId);
        var record = await context.Chapters
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == chapterId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Chapter '{chapterId}' was not found.");

        var absolutePath = Path.Combine(
            rootPath,
            record.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar));

        var linkedScenes = await context.Scenes
            .Where(scene => scene.ProjectId == projectId && scene.ChapterId == chapterId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var scene in linkedScenes)
        {
            scene.ChapterId = null;
        }

        context.Chapters.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        await RenumberAsync(projectId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Chapter> MoveUpAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken = default)
        => await MoveAsync(projectId, chapterId, direction: -1, cancellationToken).ConfigureAwait(false);

    public async Task<Chapter> MoveDownAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken = default)
        => await MoveAsync(projectId, chapterId, direction: 1, cancellationToken).ConfigureAwait(false);

    public async Task<string> LoadContentAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var chapter = await GetAsync(projectId, chapterId, cancellationToken).ConfigureAwait(false);
        try
        {
            return await _chapterFileStore.LoadAsync(rootPath, chapter, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return $"# {chapter.Title}\n\n";
        }
    }

    public async Task<Chapter> SaveContentAsync(
        Guid projectId,
        Guid chapterId,
        string markdownContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markdownContent);
        var rootPath = RequireActiveRoot(projectId);
        var chapter = await GetAsync(projectId, chapterId, cancellationToken).ConfigureAwait(false);
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
                    DraftText = markdownContent,
                },
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await _chapterFileStore.SaveAsync(rootPath, chapter, markdownContent, cancellationToken)
                .ConfigureAwait(false);
            await _journal.ClearAsync(projectId, entryId, cancellationToken).ConfigureAwait(false);
            return await GetAsync(projectId, chapterId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Journal retained for recovery.
            throw;
        }
    }

    public async Task<ManuscriptCompileResult> CompileSelectedAsync(
        Guid projectId,
        IReadOnlyList<Guid> chapterIdsInOrder,
        string exportFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chapterIdsInOrder);
        if (chapterIdsInOrder.Count == 0)
        {
            throw new InvalidOperationException("Select at least one chapter to compile.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(exportFileName);
        if (exportFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || exportFileName.Contains("..", StringComparison.Ordinal)
            || exportFileName.Contains('/')
            || exportFileName.Contains('\\'))
        {
            throw new InvalidOperationException("Export file name must be a simple file name without path segments.");
        }

        if (!exportFileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            exportFileName += ".md";
        }

        var rootPath = RequireActiveRoot(projectId);
        await _snapshots
            .CreateSafetySnapshotAsync(projectId, SafetySnapshotReason.CompilationOrExport, cancellationToken)
            .ConfigureAwait(false);

        var chapters = await GetAllAsync(projectId, cancellationToken).ConfigureAwait(false);
        var byId = chapters.ToDictionary(item => item.Id);
        var ordered = new List<Chapter>();
        foreach (var id in chapterIdsInOrder)
        {
            if (!byId.TryGetValue(id, out var chapter))
            {
                throw new InvalidOperationException($"Chapter '{id}' was not found.");
            }

            ordered.Add(chapter);
        }

        var relativePath = Path.Combine("Exports", exportFileName).Replace('\\', '/');
        var absolutePath = Path.Combine(rootPath, "Exports", exportFileName);
        if (File.Exists(absolutePath))
        {
            throw new InvalidOperationException(
                $"Export file already exists: {relativePath}. Choose a different file name.");
        }

        var builder = new StringBuilder();
        foreach (var chapter in ordered)
        {
            var content = await _chapterFileStore.LoadAsync(rootPath, chapter, cancellationToken)
                .ConfigureAwait(false);
            if (builder.Length > 0)
            {
                builder.AppendLine().AppendLine("---").AppendLine();
            }

            builder.AppendLine(content.TrimEnd()).AppendLine();
        }

        var markdown = builder.ToString();
        await AtomicFileWriter.WriteAllTextAsync(absolutePath, markdown, cancellationToken)
            .ConfigureAwait(false);

        return new ManuscriptCompileResult
        {
            AbsolutePath = absolutePath,
            RelativePath = relativePath,
            ChapterCount = ordered.Count,
            WordCount = ManuscriptTextAnalytics.CountWords(markdown),
        };
    }

    private async Task<Chapter> MoveAsync(
        Guid projectId,
        Guid chapterId,
        int direction,
        CancellationToken cancellationToken)
    {
        await using var context = Open(projectId);
        var chapters = await context.Chapters
            .Where(item => item.ProjectId == projectId)
            .OrderBy(item => item.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var index = chapters.FindIndex(item => item.Id == chapterId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Chapter '{chapterId}' was not found.");
        }

        var target = index + direction;
        if (target < 0 || target >= chapters.Count)
        {
            return ToDomain(chapters[index]);
        }

        var currentSequence = chapters[index].SequenceNumber;
        var neighborSequence = chapters[target].SequenceNumber;

        // Unique (ProjectId, SequenceNumber) requires a temporary value when swapping.
        chapters[index].SequenceNumber = -Math.Abs(currentSequence) - 1;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        chapters[target].SequenceNumber = currentSequence;
        chapters[index].SequenceNumber = neighborSequence;

        var project = await context.Projects.FirstOrDefaultAsync(item => item.Id == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is not null)
        {
            project.LastEditedUtc = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(chapters[index]);
    }

    private async Task RenumberAsync(Guid projectId, CancellationToken cancellationToken)
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
        var rootPath = RequireActiveRoot(projectId);
        return ProjectDbContextFactory.Create(Path.Combine(rootPath, ProjectPaths.DatabaseFileName));
    }

    private string RequireActiveRoot(Guid projectId)
    {
        var active = _projectService.ActiveProject
            ?? throw new InvalidOperationException("Open a project before using manuscript services.");
        if (active.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return active.RootPath;
    }

    private static string BuildRelativePath(int sequenceNumber, string title)
    {
        var stem = ManuscriptTextAnalytics.SanitizeFileStem(title);
        return $"{ProjectPaths.DraftChaptersRelativeDirectory}/{sequenceNumber:00}-{stem}.md";
    }

    private static Chapter ToDomain(ChapterRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        SequenceNumber = record.SequenceNumber,
        Title = record.Title,
        RelativeMarkdownPath = record.RelativeMarkdownPath,
        WordCount = record.WordCount,
    };
}
