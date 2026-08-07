using MasterBookWritingSystem.Core.Domain;

namespace MasterBookWritingSystem.Core.Abstractions;

/// <summary>
/// Persistence contract for book-project records inside an open project database.
/// Prefer <see cref="IProjectService"/> for create/open/close/validate of portable folders.
/// </summary>
public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(Project project, CancellationToken cancellationToken = default);
}
