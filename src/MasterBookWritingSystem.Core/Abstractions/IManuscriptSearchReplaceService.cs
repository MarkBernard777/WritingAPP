using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Manuscript;

namespace MasterBookWritingSystem.Core.Abstractions;

public sealed class ManuscriptReplacePreview
{
    public required IReadOnlyList<ManuscriptSearchHit> Hits { get; init; }

    public required int ChapterCountScanned { get; init; }
}

public sealed class ManuscriptReplaceResult
{
    public required bool Succeeded { get; init; }

    public required int ReplacementCount { get; init; }

    public required int ChapterCountChanged { get; init; }

    public required string Message { get; init; }

    public Guid? RollbackToken { get; init; }

    public string? SafetySnapshotName { get; init; }
}

public sealed class ManuscriptReplaceRollbackInfo
{
    public required Guid Token { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required int ChapterCount { get; init; }

    public required string Label { get; init; }
}

public interface IManuscriptSearchReplaceService
{
    Task<ManuscriptReplacePreview> PreviewAsync(
        Guid projectId,
        ManuscriptSearchOptions options,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task<ManuscriptReplaceResult> ApplyAsync(
        Guid projectId,
        ManuscriptSearchOptions options,
        IReadOnlyList<ManuscriptSearchHit> includedHits,
        bool confirmed,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task<ManuscriptReplaceRollbackInfo?> GetLastRollbackAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task RollbackAsync(
        Guid projectId,
        Guid rollbackToken,
        bool confirmed,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);
}
