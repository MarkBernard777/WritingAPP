namespace MasterBookWritingSystem.Core.Abstractions;

/// <summary>
/// Coordinates chapter Markdown scene-delimiter rewrites with recovery journaling.
/// File writes happen before related SQLite metadata commits when both change.
/// </summary>
public interface ISceneProseService
{
    /// <summary>
    /// Journal + rewrite chapter file(s) for a scene chapter assignment change (unwrap source, append empty target).
    /// Call before committing the SQLite chapter-id change.
    /// </summary>
    Task SyncAssignmentFilesAsync(
        Guid projectId,
        Guid sceneId,
        Guid? fromChapterId,
        Guid? toChapterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Journal + unwrap a scene's markers from its chapter file, keeping prose. Call before deleting the scene row.
    /// </summary>
    Task UnwrapSceneFileAsync(
        Guid projectId,
        Guid chapterId,
        Guid sceneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears journals written for the given chapter ids after a successful DB commit.
    /// </summary>
    Task ClearChapterJournalsAsync(
        Guid projectId,
        IEnumerable<Guid> chapterIds,
        CancellationToken cancellationToken = default);
}
