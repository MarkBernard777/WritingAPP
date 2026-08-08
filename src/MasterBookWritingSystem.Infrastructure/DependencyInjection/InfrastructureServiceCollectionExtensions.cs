using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Infrastructure.Dashboard;
using MasterBookWritingSystem.Infrastructure.Backup;
using MasterBookWritingSystem.Infrastructure.Documents;
using MasterBookWritingSystem.Infrastructure.Manuscript;
using MasterBookWritingSystem.Infrastructure.Paths;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Publishing;
using MasterBookWritingSystem.Infrastructure.Recovery;
using MasterBookWritingSystem.Infrastructure.Settings;
using MasterBookWritingSystem.Infrastructure.Story;
using MasterBookWritingSystem.Infrastructure.Tools;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MasterBookWritingSystem.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IApplicationPaths, LocalApplicationPaths>();
        services.AddSingleton<IApplicationSettingsStore, JsonApplicationSettingsStore>();
        services.AddSingleton<IProjectSnapshotWriter, ProjectSnapshotWriter>();
        services.AddSingleton<IWorkflowDefinitionSource, EmbeddedOrSeedWorkflowDefinitionSource>();
        services.AddSingleton<IDocumentTemplateCatalog, SeedDocumentTemplateCatalog>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IProjectIntegrityService, SqliteProjectIntegrityService>();
        services.AddSingleton<IRecoveryJournalService, FileRecoveryJournalService>();
        services.AddSingleton<IRecoveryCentreService, RecoveryCentreService>();
        services.AddSingleton<ISaveStateService, SaveStateService>();
        services.AddSingleton<IChapterFileStore, ChapterFileStore>();
        services.AddSingleton<ISceneProseService, SceneProseService>();
        services.AddSingleton<IChapterService, ChapterService>();
        services.AddSingleton<IManuscriptHierarchyService, ManuscriptHierarchyService>();
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<IEditorAutosaveService, DebouncedEditorAutosaveService>();
        services.AddSingleton<IIdeaService, IdeaService>();
        services.AddSingleton<IToolsReportExporter, ToolsReportExporter>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<ISnapshotService, SnapshotService>();
        services.AddSingleton<IPortablePackageService, PortablePackageService>();
        services.AddSingleton<IStoryChangeNotifier, StoryChangeNotifier>();
        services.AddSingleton<IStoryDataService, StoryDataService>();
        services.AddSingleton<IPublishingService, PublishingService>();
        services.AddSingleton<IWorkflowService, WorkflowService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        return services;
    }
}
