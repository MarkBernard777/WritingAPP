using MasterBookWritingSystem.Core.Domain.Manuscript;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IChapterFileStore
{
    Task SaveAsync(
        string projectRootPath,
        Chapter chapter,
        string markdownContent,
        CancellationToken cancellationToken = default);

    Task<string> LoadAsync(
        string projectRootPath,
        Chapter chapter,
        CancellationToken cancellationToken = default);
}
