namespace MasterBookWritingSystem.Core.Abstractions;

public interface IProjectIntegrityService
{
    Task<ProjectIntegrityResult> CheckAsync(
        string projectRootPath,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectIntegrityResult
{
    public required bool IsHealthy { get; init; }

    public required string Summary { get; init; }

    public required DateTimeOffset CheckedUtc { get; init; }

    public IReadOnlyList<string> Details { get; init; } = [];
}
