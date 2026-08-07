using MasterBookWritingSystem.Core.Domain;

namespace MasterBookWritingSystem.Core.Abstractions;

/// <summary>
/// Persistence contract for book projects. SQLite implementation arrives in Milestone 2.
/// </summary>
public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(Project project, CancellationToken cancellationToken = default);
}
