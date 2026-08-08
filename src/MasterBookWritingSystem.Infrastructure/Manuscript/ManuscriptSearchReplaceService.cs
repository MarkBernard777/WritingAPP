using System.Text.Json;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.Persistence;

namespace MasterBookWritingSystem.Infrastructure.Manuscript;

public sealed class ManuscriptSearchReplaceService : IManuscriptSearchReplaceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IManuscriptHierarchyService _hierarchy;
    private readonly ISnapshotService _snapshots;
    private readonly IRecoveryJournalService _journal;
    private readonly ITextFileIO _files;

    public ManuscriptSearchReplaceService(
        IProjectService projects,
        IChapterService chapters,
        IManuscriptHierarchyService hierarchy,
        ISnapshotService snapshots,
        IRecoveryJournalService journal,
        ITextFileIO files)
    {
        _projects = projects;
        _chapters = chapters;
        _hierarchy = hierarchy;
        _snapshots = snapshots;
        _journal = journal;
        _files = files;
    }

    public async Task<ManuscriptReplacePreview> PreviewAsync(
        Guid projectId,
        ManuscriptSearchOptions options,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        ManuscriptSearchMatching.ValidateOptions(options);
        var chapters = await ResolveScopedChaptersAsync(projectId, options, cancellationToken)
            .ConfigureAwait(false);
        var hits = new List<ManuscriptSearchHit>();
        var scanned = 0;
        foreach (var chapter in chapters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;
            progress?.Report(new OperationProgress
            {
                Message = $"Scanning {chapter.Title}…",
                PercentComplete = 100.0 * scanned / Math.Max(1, chapters.Count),
            });
            var markdown = await _chapters.LoadContentAsync(projectId, chapter.Id, cancellationToken)
                .ConfigureAwait(false);
            hits.AddRange(ManuscriptSearchMatching.FindInChapter(
                chapter.Id,
                chapter.Title,
                chapter.SequenceNumber,
                markdown,
                options));
        }

        return new ManuscriptReplacePreview
        {
            Hits = hits,
            ChapterCountScanned = scanned,
        };
    }

    public async Task<ManuscriptReplaceResult> ApplyAsync(
        Guid projectId,
        ManuscriptSearchOptions options,
        IReadOnlyList<ManuscriptSearchHit> includedHits,
        bool confirmed,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(includedHits);
        if (!confirmed)
        {
            throw new InvalidOperationException("Bulk replace requires explicit confirmation after preview.");
        }

        var selected = includedHits.Where(hit => hit.Include).ToList();
        if (selected.Count == 0)
        {
            return new ManuscriptReplaceResult
            {
                Succeeded = true,
                ReplacementCount = 0,
                ChapterCountChanged = 0,
                Message = "No replacements were included.",
            };
        }

        var root = RequireRoot(projectId);
        var byChapter = selected.GroupBy(hit => hit.ChapterId).ToList();
        progress?.Report(new OperationProgress { Message = "Creating safety snapshot…", PercentComplete = 5 });
        var snapshot = await _snapshots
            .CreateSafetySnapshotAsync(projectId, SafetySnapshotReason.BulkSearchReplace, cancellationToken, progress)
            .ConfigureAwait(false);

        var originals = new Dictionary<Guid, (Chapter Chapter, string Markdown, string AbsolutePath)>();
        var written = new List<Guid>();
        var rollbackToken = Guid.NewGuid();

        try
        {
            var index = 0;
            foreach (var group in byChapter)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                progress?.Report(new OperationProgress
                {
                    Message = $"Preparing chapter {index}/{byChapter.Count}…",
                    PercentComplete = 10 + (40.0 * index / byChapter.Count),
                });

                var chapter = await _chapters.GetAsync(projectId, group.Key, cancellationToken)
                    .ConfigureAwait(false);
                var markdown = await _chapters.LoadContentAsync(projectId, chapter.Id, cancellationToken)
                    .ConfigureAwait(false);
                var absolute = ResolveChapterPath(root, chapter);
                originals[chapter.Id] = (chapter, markdown, absolute);

                var entryId = RecoveryJournalIds.Create(
                    projectId,
                    RecoveryEntityType.ManuscriptChapter,
                    chapter.Id);
                await _journal.UpsertAsync(
                        new RecoveryJournalEntry
                        {
                            EntryId = entryId,
                            ProjectId = projectId,
                            EntityType = RecoveryEntityType.ManuscriptChapter,
                            EntityId = chapter.Id,
                            DraftText = markdown,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            index = 0;
            foreach (var group in byChapter)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                progress?.Report(new OperationProgress
                {
                    Message = $"Writing chapter {index}/{byChapter.Count}…",
                    PercentComplete = 50 + (40.0 * index / byChapter.Count),
                });

                var (chapter, markdown, _) = originals[group.Key];
                var next = ManuscriptSearchMatching.ApplyHitsToChapter(markdown, group);
                await _chapters.SaveContentAsync(projectId, chapter.Id, next, cancellationToken)
                    .ConfigureAwait(false);
                written.Add(chapter.Id);
            }

            await PersistRollbackAsync(root, projectId, rollbackToken, originals, selected.Count, cancellationToken)
                .ConfigureAwait(false);

            foreach (var chapterId in originals.Keys)
            {
                var entryId = RecoveryJournalIds.Create(
                    projectId,
                    RecoveryEntityType.ManuscriptChapter,
                    chapterId);
                await _journal.ClearAsync(projectId, entryId, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new OperationProgress { Message = "Replace complete.", PercentComplete = 100 });
            return new ManuscriptReplaceResult
            {
                Succeeded = true,
                ReplacementCount = selected.Count,
                ChapterCountChanged = written.Count,
                Message =
                    $"Applied {selected.Count} replacement(s) across {written.Count} chapter(s). "
                    + "Use one-operation rollback if needed.",
                RollbackToken = rollbackToken,
                SafetySnapshotName = snapshot.Snapshot?.Name,
            };
        }
        catch (OperationCanceledException)
        {
            // Do not honour the cancelled token while restoring — compensation must finish.
            await CompensateAsync(projectId, originals, written, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await CompensateAsync(projectId, originals, written, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ManuscriptReplaceRollbackInfo?> GetLastRollbackAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var root = RequireRoot(projectId);
        var dir = Path.Combine(root, ProjectPaths.SearchReplaceRollbackRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var latest = Directory.GetDirectories(dir)
            .Select(path => path)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (latest is null)
        {
            return null;
        }

        var manifestPath = Path.Combine(latest, "rollback-manifest.json");
        if (!_files.FileExists(manifestPath))
        {
            return null;
        }

        var json = await _files.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<RollbackManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("Rollback manifest was empty.");
        return new ManuscriptReplaceRollbackInfo
        {
            Token = manifest.Token,
            CreatedUtc = manifest.CreatedUtc,
            ChapterCount = manifest.Chapters.Count,
            Label = manifest.Label,
        };
    }

    public async Task RollbackAsync(
        Guid projectId,
        Guid rollbackToken,
        bool confirmed,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException("Rollback requires explicit confirmation.");
        }

        var root = RequireRoot(projectId);
        var dir = Path.Combine(
            root,
            ProjectPaths.SearchReplaceRollbackRelativeDirectory.Replace('/', Path.DirectorySeparatorChar),
            rollbackToken.ToString("N"));
        var manifestPath = Path.Combine(dir, "rollback-manifest.json");
        if (!_files.FileExists(manifestPath))
        {
            throw new InvalidOperationException("Rollback package was not found.");
        }

        progress?.Report(new OperationProgress { Message = "Rolling back replacements…", PercentComplete = 10 });
        var json = await _files.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<RollbackManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("Rollback manifest was empty.");

        var index = 0;
        foreach (var chapter in manifest.Chapters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report(new OperationProgress
            {
                Message = $"Restoring {chapter.Title}…",
                PercentComplete = 10 + (80.0 * index / Math.Max(1, manifest.Chapters.Count)),
            });
            var contentPath = Path.Combine(dir, chapter.ContentFileName);
            var content = await _files.ReadAllTextAsync(contentPath, cancellationToken).ConfigureAwait(false);
            await _chapters.SaveContentAsync(projectId, chapter.ChapterId, content, cancellationToken)
                .ConfigureAwait(false);
        }

        // One-operation rollback: remove the package after success.
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }

        progress?.Report(new OperationProgress { Message = "Rollback complete.", PercentComplete = 100 });
    }

    private async Task CompensateAsync(
        Guid projectId,
        Dictionary<Guid, (Chapter Chapter, string Markdown, string AbsolutePath)> originals,
        List<Guid> written,
        CancellationToken cancellationToken)
    {
        foreach (var chapterId in written)
        {
            if (!originals.TryGetValue(chapterId, out var original))
            {
                continue;
            }

            try
            {
                await _chapters.SaveContentAsync(projectId, chapterId, original.Markdown, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Journals retain evidence for Recovery Centre.
            }
        }
    }

    private async Task PersistRollbackAsync(
        string root,
        Guid projectId,
        Guid token,
        Dictionary<Guid, (Chapter Chapter, string Markdown, string AbsolutePath)> originals,
        int replacementCount,
        CancellationToken cancellationToken)
    {
        var dir = Path.Combine(
            root,
            ProjectPaths.SearchReplaceRollbackRelativeDirectory.Replace('/', Path.DirectorySeparatorChar),
            token.ToString("N"));
        Directory.CreateDirectory(dir);
        var chapters = new List<RollbackChapterEntry>();
        foreach (var pair in originals)
        {
            var fileName = pair.Key.ToString("N") + ".md";
            await _files.WriteAllTextAsync(Path.Combine(dir, fileName), pair.Value.Markdown, cancellationToken)
                .ConfigureAwait(false);
            chapters.Add(new RollbackChapterEntry
            {
                ChapterId = pair.Key,
                Title = pair.Value.Chapter.Title,
                ContentFileName = fileName,
            });
        }

        var manifest = new RollbackManifest
        {
            Token = token,
            ProjectId = projectId,
            CreatedUtc = DateTimeOffset.UtcNow,
            Label = $"{replacementCount} replacement(s) across {chapters.Count} chapter(s)",
            Chapters = chapters,
        };
        await _files.WriteAllTextAsync(
                Path.Combine(dir, "rollback-manifest.json"),
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);

        // Keep only the latest rollback package.
        var parent = Path.GetDirectoryName(dir)!;
        foreach (var old in Directory.GetDirectories(parent)
                     .Where(path => !string.Equals(path, dir, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task<IReadOnlyList<Chapter>> ResolveScopedChaptersAsync(
        Guid projectId,
        ManuscriptSearchOptions options,
        CancellationToken cancellationToken)
    {
        var all = await _chapters.GetAllAsync(projectId, cancellationToken).ConfigureAwait(false);
        return options.ScopeKind switch
        {
            ManuscriptSearchScopeKind.EntireManuscript => all,
            ManuscriptSearchScopeKind.SelectedChapters => all
                .Where(chapter => options.SelectedChapterIds.Contains(chapter.Id))
                .OrderBy(chapter => chapter.SequenceNumber)
                .ToList(),
            ManuscriptSearchScopeKind.Book => await FilterByBookAsync(projectId, options.BookId!.Value, all, cancellationToken)
                .ConfigureAwait(false),
            ManuscriptSearchScopeKind.Part => all
                .Where(chapter => chapter.PartId == options.PartId)
                .OrderBy(chapter => chapter.SequenceNumber)
                .ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(options.ScopeKind)),
        };
    }

    private async Task<IReadOnlyList<Chapter>> FilterByBookAsync(
        Guid projectId,
        Guid bookId,
        IReadOnlyList<Chapter> all,
        CancellationToken cancellationToken)
    {
        var tree = await _hierarchy.GetHierarchyAsync(projectId, cancellationToken).ConfigureAwait(false);
        var partIds = tree.Books
            .Where(book => book.Id == bookId)
            .SelectMany(book => book.Parts)
            .Select(part => part.Id)
            .ToHashSet();
        return all
            .Where(chapter => chapter.PartId is { } partId && partIds.Contains(partId))
            .OrderBy(chapter => chapter.SequenceNumber)
            .ToList();
    }

    private static string ResolveChapterPath(string root, Chapter chapter)
        => Path.Combine(
            root,
            chapter.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar));

    private string RequireRoot(Guid projectId)
    {
        var project = _projects.ActiveProject
            ?? throw new InvalidOperationException("Open a project before searching the manuscript.");
        if (project.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return project.RootPath;
    }

    private sealed class RollbackManifest
    {
        public Guid Token { get; set; }

        public Guid ProjectId { get; set; }

        public DateTimeOffset CreatedUtc { get; set; }

        public string Label { get; set; } = string.Empty;

        public List<RollbackChapterEntry> Chapters { get; set; } = [];
    }

    private sealed class RollbackChapterEntry
    {
        public Guid ChapterId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string ContentFileName { get; set; } = string.Empty;
    }
}
