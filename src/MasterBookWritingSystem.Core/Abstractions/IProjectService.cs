using MasterBookWritingSystem.Core.Domain;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IProjectService
{
    Project? ActiveProject { get; }

    Task<Project> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<Project> OpenAsync(string projectRootPath, CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);

    Task<ProjectValidationResult> ValidateAsync(string projectRootPath, CancellationToken cancellationToken = default);
}
