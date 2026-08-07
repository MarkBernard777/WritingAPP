using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Infrastructure.Dashboard;
using MasterBookWritingSystem.Infrastructure.Backup;
using MasterBookWritingSystem.Infrastructure.Documents;
using MasterBookWritingSystem.Infrastructure.Manuscript;
using MasterBookWritingSystem.Infrastructure.Paths;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Story;
using MasterBookWritingSystem.Infrastructure.Tools;
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
        services.AddSingleton<IDocumentTemplateCatalog, SeedDocumentTemplateCatalog>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IChapterFileStore, ChapterFileStore>();
        services.AddSingleton<IChapterService, ChapterService>();
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<IIdeaService, IdeaService>();
        services.AddSingleton<IToolsReportExporter, ToolsReportExporter>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<ISnapshotService, SnapshotService>();
        services.AddSingleton<IPortablePackageService, PortablePackageService>();
        services.AddSingleton<IStoryDataService, StoryDataService>();
        services.AddSingleton<IWorkflowService, WorkflowService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        return services;
    }
}
