using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Hierarchy;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IManuscriptHierarchyService
{
    Task<ManuscriptHierarchy> GetHierarchyAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task EnsureDefaultHierarchyAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<Book> CreateBookAsync(
        Guid projectId,
        string title,
        CancellationToken cancellationToken = default);

    Task<Book> RenameBookAsync(
        Guid projectId,
        Guid bookId,
        string title,
        CancellationToken cancellationToken = default);

    Task DeleteBookAsync(
        Guid projectId,
        Guid bookId,
        HierarchyDeletionPolicy? policy = null,
        CancellationToken cancellationToken = default);

    Task<Book> MoveBookAsync(
        Guid projectId,
        Guid bookId,
        int direction,
        CancellationToken cancellationToken = default);

    Task<Book> ReorderBookAsync(
        Guid projectId,
        Guid bookId,
        int newSequenceNumber,
        CancellationToken cancellationToken = default);

    Task<Part> CreatePartAsync(
        Guid projectId,
        Guid bookId,
        string title,
        CancellationToken cancellationToken = default);

    Task<Part> RenamePartAsync(
        Guid projectId,
        Guid partId,
        string title,
        CancellationToken cancellationToken = default);

    Task DeletePartAsync(
        Guid projectId,
        Guid partId,
        HierarchyDeletionPolicy? policy = null,
        CancellationToken cancellationToken = default);

    Task<Part> MovePartAsync(
        Guid projectId,
        Guid partId,
        int direction,
        CancellationToken cancellationToken = default);

    Task<Chapter> MoveChapterToPartAsync(
        Guid projectId,
        Guid chapterId,
        Guid? partId,
        CancellationToken cancellationToken = default);

    Task<Scene> AssignSceneToChapterAsync(
        Guid projectId,
        Guid sceneId,
        Guid chapterId,
        CancellationToken cancellationToken = default);

    Task<Scene> UnassignSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default);

    Task<Scene> MoveSceneToChapterAsync(
        Guid projectId,
        Guid sceneId,
        Guid? chapterId,
        CancellationToken cancellationToken = default);

    Task<Scene> MoveSceneAsync(
        Guid projectId,
        Guid sceneId,
        int direction,
        CancellationToken cancellationToken = default);

    Task<Guid?> GetDefaultPartIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
