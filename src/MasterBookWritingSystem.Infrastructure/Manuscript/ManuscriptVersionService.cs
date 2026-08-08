using System.Text.Json;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Manuscript;

namespace MasterBookWritingSystem.Infrastructure.Manuscript;

public sealed class ManuscriptVersionService : IManuscriptVersionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly ISnapshotService _snapshots;
    private readonly ITextFileIO _files;

    public ManuscriptVersionService(
        IProjectService projects,
        IChapterService chapters,
        ISnapshotService snapshots,
        ITextFileIO files)
    {
        _projects = projects;
        _chapters = chapters;
        _snapshots = snapshots;
        _files = files;
    }

    public async Task<ManuscriptVersionInfo> CreateAsync(
        Guid projectId,
        string label,
        string? notes = null,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var root = RequireRoot(projectId);
        var chapters = await _chapters.GetAllAsync(projectId, cancellationToken).ConfigureAwait(false);
        var id = Guid.NewGuid();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safeLabel = ManuscriptTextAnalytics.SanitizeFileStem(label);
        var directoryName = $"{stamp}-{safeLabel}-{id.ToString("N")[..8]}";
        var absolute = Path.Combine(
            root,
            ProjectPaths.ManuscriptVersionsRelativeDirectory.Replace('/', Path.DirectorySeparatorChar),
            directoryName);
        Directory.CreateDirectory(absolute);

        var entries = new List<VersionChapterEntry>();
        var index = 0;
        foreach (var chapter in chapters.OrderBy(item => item.SequenceNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report(new OperationProgress
            {
                Message = $"Versioning {chapter.Title}…",
                PercentComplete = 100.0 * index / Math.Max(1, chapters.Count),
            });
            var markdown = await _chapters.LoadContentAsync(projectId, chapter.Id, cancellationToken)
                .ConfigureAwait(false);
            var fileName = $"{chapter.SequenceNumber:00}-{chapter.Id:N}.md";
            await _files.WriteAllTextAsync(Path.Combine(absolute, fileName), markdown, cancellationToken)
                .ConfigureAwait(false);
            entries.Add(new VersionChapterEntry
            {
                ChapterId = chapter.Id,
                Title = chapter.Title,
                SequenceNumber = chapter.SequenceNumber,
                RelativeMarkdownPath = chapter.RelativeMarkdownPath,
                ContentFileName = fileName,
                Sha256 = ChecksumHelper.Sha256Bytes(System.Text.Encoding.UTF8.GetBytes(markdown)),
            });
        }

        var manifest = new VersionManifest
        {
            Id = id,
            ProjectId = projectId,
            Label = label.Trim(),
            Notes = notes?.Trim() ?? string.Empty,
            CreatedUtc = DateTimeOffset.UtcNow,
            DirectoryName = directoryName,
            Chapters = entries,
        };
        await _files.WriteAllTextAsync(
                Path.Combine(absolute, "version-manifest.json"),
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);

        return ToInfo(manifest, absolute);
    }

    public async Task<IReadOnlyList<ManuscriptVersionInfo>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var root = RequireRoot(projectId);
        var baseDir = Path.Combine(
            root,
            ProjectPaths.ManuscriptVersionsRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(baseDir))
        {
            return [];
        }

        var results = new List<ManuscriptVersionInfo>();
        foreach (var dir in Directory.GetDirectories(baseDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(dir, "version-manifest.json");
            if (!_files.FileExists(manifestPath))
            {
                continue;
            }

            var json = await _files.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions);
            if (manifest is null || manifest.ProjectId != projectId)
            {
                continue;
            }

            results.Add(ToInfo(manifest, dir));
        }

        return results
            .OrderByDescending(item => item.CreatedUtc)
            .ToList();
    }

    public async Task<ManuscriptVersionDiff> DiffAgainstCurrentAsync(
        Guid projectId,
        Guid versionId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        var (manifest, absolute) = await LoadManifestAsync(projectId, versionId, cancellationToken)
            .ConfigureAwait(false);
        var current = (await _chapters.GetAllAsync(projectId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(item => item.Id);
        var lines = new List<ManuscriptVersionDiffLine>();
        var index = 0;
        foreach (var entry in manifest.Chapters.OrderBy(item => item.SequenceNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report(new OperationProgress
            {
                Message = $"Diffing {entry.Title}…",
                PercentComplete = 100.0 * index / Math.Max(1, manifest.Chapters.Count),
            });

            var versionText = await _files
                .ReadAllTextAsync(Path.Combine(absolute, entry.ContentFileName), cancellationToken)
                .ConfigureAwait(false);
            if (!current.TryGetValue(entry.ChapterId, out var chapter))
            {
                lines.Add(new ManuscriptVersionDiffLine
                {
                    Kind = "removed",
                    Text = $"Chapter '{entry.Title}' exists only in the version.",
                    ChapterId = entry.ChapterId,
                    ChapterTitle = entry.Title,
                });
                continue;
            }

            var live = await _chapters.LoadContentAsync(projectId, chapter.Id, cancellationToken)
                .ConfigureAwait(false);
            if (string.Equals(versionText, live, StringComparison.Ordinal))
            {
                lines.Add(new ManuscriptVersionDiffLine
                {
                    Kind = "same",
                    Text = $"{entry.Title}: unchanged",
                    ChapterId = entry.ChapterId,
                    ChapterTitle = entry.Title,
                });
                continue;
            }

            AppendReadableDiff(lines, entry.Title, entry.ChapterId, versionText, live);
        }

        foreach (var liveChapter in current.Values.OrderBy(item => item.SequenceNumber))
        {
            if (manifest.Chapters.Any(item => item.ChapterId == liveChapter.Id))
            {
                continue;
            }

            lines.Add(new ManuscriptVersionDiffLine
            {
                Kind = "added",
                Text = $"Chapter '{liveChapter.Title}' exists only in the current manuscript.",
                ChapterId = liveChapter.Id,
                ChapterTitle = liveChapter.Title,
            });
        }

        return new ManuscriptVersionDiff
        {
            Version = ToInfo(manifest, absolute),
            Lines = lines,
        };
    }

    public async Task RestoreAsync(
        Guid projectId,
        Guid versionId,
        bool confirmed,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException("Version restore requires explicit confirmation.");
        }

        var (manifest, absolute) = await LoadManifestAsync(projectId, versionId, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new OperationProgress { Message = "Creating safety snapshot…", PercentComplete = 5 });
        await _snapshots
            .CreateSafetySnapshotAsync(projectId, SafetySnapshotReason.ManuscriptVersionRestore, cancellationToken, progress)
            .ConfigureAwait(false);

        var index = 0;
        foreach (var entry in manifest.Chapters.OrderBy(item => item.SequenceNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report(new OperationProgress
            {
                Message = $"Restoring {entry.Title}…",
                PercentComplete = 10 + (85.0 * index / Math.Max(1, manifest.Chapters.Count)),
            });
            var content = await _files
                .ReadAllTextAsync(Path.Combine(absolute, entry.ContentFileName), cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await _chapters.SaveContentAsync(projectId, entry.ChapterId, content, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Chapter removed since version — skip rather than inventing a new chapter silently.
                progress?.Report(new OperationProgress
                {
                    Message = $"Skipped missing chapter '{entry.Title}'.",
                    PercentComplete = 10 + (85.0 * index / Math.Max(1, manifest.Chapters.Count)),
                });
            }
        }

        progress?.Report(new OperationProgress { Message = "Version restore complete.", PercentComplete = 100 });
    }

    private static void AppendReadableDiff(
        List<ManuscriptVersionDiffLine> lines,
        string title,
        Guid chapterId,
        string before,
        string after)
    {
        var beforeLines = before.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var afterLines = after.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var max = Math.Max(beforeLines.Length, afterLines.Length);
        lines.Add(new ManuscriptVersionDiffLine
        {
            Kind = "header",
            Text = $"--- {title} ---",
            ChapterId = chapterId,
            ChapterTitle = title,
        });
        for (var i = 0; i < max; i++)
        {
            var left = i < beforeLines.Length ? beforeLines[i] : null;
            var right = i < afterLines.Length ? afterLines[i] : null;
            if (string.Equals(left, right, StringComparison.Ordinal))
            {
                continue;
            }

            if (left is not null)
            {
                lines.Add(new ManuscriptVersionDiffLine
                {
                    Kind = "before",
                    Text = "- " + left,
                    ChapterId = chapterId,
                    ChapterTitle = title,
                });
            }

            if (right is not null)
            {
                lines.Add(new ManuscriptVersionDiffLine
                {
                    Kind = "after",
                    Text = "+ " + right,
                    ChapterId = chapterId,
                    ChapterTitle = title,
                });
            }
        }
    }

    private async Task<(VersionManifest Manifest, string AbsolutePath)> LoadManifestAsync(
        Guid projectId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var versions = await ListAsync(projectId, cancellationToken).ConfigureAwait(false);
        var info = versions.FirstOrDefault(item => item.Id == versionId)
            ?? throw new InvalidOperationException($"Manuscript version '{versionId}' was not found.");
        var json = await _files
            .ReadAllTextAsync(Path.Combine(info.AbsolutePath, "version-manifest.json"), cancellationToken)
            .ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("Version manifest was empty.");
        return (manifest, info.AbsolutePath);
    }

    private static ManuscriptVersionInfo ToInfo(VersionManifest manifest, string absolute)
        => new()
        {
            Id = manifest.Id,
            Label = manifest.Label,
            DirectoryName = manifest.DirectoryName,
            AbsolutePath = absolute,
            CreatedUtc = manifest.CreatedUtc,
            ChapterCount = manifest.Chapters.Count,
            Notes = manifest.Notes,
        };

    private string RequireRoot(Guid projectId)
    {
        var project = _projects.ActiveProject
            ?? throw new InvalidOperationException("Open a project before managing manuscript versions.");
        if (project.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return project.RootPath;
    }

    private sealed class VersionManifest
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }

        public string Label { get; set; } = string.Empty;

        public string Notes { get; set; } = string.Empty;

        public DateTimeOffset CreatedUtc { get; set; }

        public string DirectoryName { get; set; } = string.Empty;

        public List<VersionChapterEntry> Chapters { get; set; } = [];
    }

    private sealed class VersionChapterEntry
    {
        public Guid ChapterId { get; set; }

        public string Title { get; set; } = string.Empty;

        public int SequenceNumber { get; set; }

        public string RelativeMarkdownPath { get; set; } = string.Empty;

        public string ContentFileName { get; set; } = string.Empty;

        public string Sha256 { get; set; } = string.Empty;
    }
}
