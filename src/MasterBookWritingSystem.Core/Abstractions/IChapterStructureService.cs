using MasterBookWritingSystem.Core.Domain.Manuscript;

namespace MasterBookWritingSystem.Core.Abstractions;

public sealed class ChapterSplitResult
{
    public required Chapter LeftChapter { get; init; }

    public required Chapter RightChapter { get; init; }
}

public sealed class ChapterMergeResult
{
    public required Chapter SurvivingChapter { get; init; }

    public required Guid RemovedChapterId { get; init; }
}

public interface IChapterStructureService
{
    /// <summary>
    /// Splits a chapter at the caret/selection start. Rejects splits that cut through a scene marker span.
    /// </summary>
    Task<ChapterSplitResult> SplitChapterAtAsync(
        Guid projectId,
        Guid chapterId,
        int splitIndex,
        string? rightChapterTitle = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges two adjacent chapters (by sequence). Preserves prose order and scene associations.
    /// </summary>
    Task<ChapterMergeResult> MergeAdjacentChaptersAsync(
        Guid projectId,
        Guid leftChapterId,
        Guid rightChapterId,
        CancellationToken cancellationToken = default);
}
