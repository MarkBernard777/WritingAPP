using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Manuscript;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IChapterService
{
    Task<IReadOnlyList<Chapter>> GetAllAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<Chapter> GetAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken = default);

    Task<Chapter> CreateAsync(
        Guid projectId,
        string title,
        CancellationToken cancellationToken = default);

    Task<Chapter> RenameAsync(
        Guid projectId,
        Guid chapterId,
        string title,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken = default);

    Task<Chapter> MoveUpAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken = default);

    Task<Chapter> MoveDownAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken = default);

    Task<string> LoadContentAsync(
        Guid projectId,
        Guid chapterId,
        CancellationToken cancellationToken = default);

    Task<Chapter> SaveContentAsync(
        Guid projectId,
        Guid chapterId,
        string markdownContent,
        CancellationToken cancellationToken = default);

    Task<ManuscriptCompileResult> CompileSelectedAsync(
        Guid projectId,
        IReadOnlyList<Guid> chapterIdsInOrder,
        string exportFileName,
        CancellationToken cancellationToken = default);
}
