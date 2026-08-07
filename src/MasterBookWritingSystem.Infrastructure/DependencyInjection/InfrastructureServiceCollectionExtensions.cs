using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Infrastructure.Dashboard;
using MasterBookWritingSystem.Infrastructure.Paths;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IApplicationPaths, LocalApplicationPaths>();
        services.AddSingleton<IWorkflowDefinitionSource, EmbeddedOrSeedWorkflowDefinitionSource>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IChapterFileStore, ChapterFileStore>();
        services.AddSingleton<IWorkflowService, WorkflowService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        return services;
    }
}
