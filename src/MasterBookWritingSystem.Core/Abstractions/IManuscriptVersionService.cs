using MasterBookWritingSystem.Core.Backup;

namespace MasterBookWritingSystem.Core.Abstractions;

public sealed class ManuscriptVersionInfo
{
    public required Guid Id { get; init; }

    public required string Label { get; init; }

    public required string DirectoryName { get; init; }

    public required string AbsolutePath { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required int ChapterCount { get; init; }

    public required string Notes { get; init; }
}

public sealed class ManuscriptVersionDiffLine
{
    public required string Kind { get; init; }

    public required string Text { get; init; }

    public Guid? ChapterId { get; init; }

    public string? ChapterTitle { get; init; }
}

public sealed class ManuscriptVersionDiff
{
    public required ManuscriptVersionInfo Version { get; init; }

    public required IReadOnlyList<ManuscriptVersionDiffLine> Lines { get; init; }
}

public interface IManuscriptVersionService
{
    Task<ManuscriptVersionInfo> CreateAsync(
        Guid projectId,
        string label,
        string? notes = null,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task<IReadOnlyList<ManuscriptVersionInfo>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<ManuscriptVersionDiff> DiffAgainstCurrentAsync(
        Guid projectId,
        Guid versionId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task RestoreAsync(
        Guid projectId,
        Guid versionId,
        bool confirmed,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);
}
