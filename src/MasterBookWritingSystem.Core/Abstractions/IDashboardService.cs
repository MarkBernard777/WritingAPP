using MasterBookWritingSystem.Core.Dashboard;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IDashboardService
{
    Task<ProjectDashboardSummary?> BuildAsync(CancellationToken cancellationToken = default);
}
