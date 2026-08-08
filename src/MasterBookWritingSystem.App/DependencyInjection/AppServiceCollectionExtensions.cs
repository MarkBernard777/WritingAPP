using MasterBookWritingSystem.App.Navigation;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.App.ViewModels;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.App.DependencyInjection;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddInfrastructure();

        services.AddSingleton<IProjectDialogService, WpfProjectDialogService>();
        services.AddSingleton<IRecentProjectsStore, RecentProjectsStore>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<WorkflowViewModel>();
        services.AddTransient<DocumentsViewModel>();
        services.AddTransient<SceneCorkboardViewModel>();
        services.AddTransient<ManuscriptViewModel>();
        services.AddTransient<StoryDataViewModel>();
        services.AddTransient<ToolsViewModel>();
        services.AddTransient<PublishingViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<RecoveryViewModel>();

        return services;
    }
}
