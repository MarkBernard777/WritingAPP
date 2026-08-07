using CommunityToolkit.Mvvm.ComponentModel;
using MasterBookWritingSystem.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.App.Navigation;

public sealed class NavigationService : ObservableObject, INavigationService
{
    private readonly IServiceProvider _services;
    private ObservableObject _currentViewModel = null!;
    private AppSection _currentSection;
    private Guid? _pendingSceneId;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
        NavigateTo(AppSection.Dashboard);
    }

    public ObservableObject CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public AppSection CurrentSection
    {
        get => _currentSection;
        private set => SetProperty(ref _currentSection, value);
    }

    public void NavigateToStoryDataScene(Guid sceneId)
    {
        _pendingSceneId = sceneId;
        NavigateTo(AppSection.StoryData);
    }

    public void NavigateTo(AppSection section)
    {
        CurrentViewModel = section switch
        {
            AppSection.Dashboard => _services.GetRequiredService<DashboardViewModel>(),
            AppSection.Workflow => _services.GetRequiredService<WorkflowViewModel>(),
            AppSection.Documents => _services.GetRequiredService<DocumentsViewModel>(),
            AppSection.Manuscript => _services.GetRequiredService<ManuscriptViewModel>(),
            AppSection.StoryData => CreateStoryDataViewModel(),
            AppSection.Tools => _services.GetRequiredService<ToolsViewModel>(),
            AppSection.Publishing => _services.GetRequiredService<PublishingViewModel>(),
            AppSection.Settings => _services.GetRequiredService<SettingsViewModel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };
        CurrentSection = section;
    }

    private StoryDataViewModel CreateStoryDataViewModel()
    {
        var viewModel = _services.GetRequiredService<StoryDataViewModel>();
        if (_pendingSceneId is { } sceneId)
        {
            var pending = sceneId;
            _pendingSceneId = null;
            _ = viewModel.FocusSceneAsync(pending);
        }

        return viewModel;
    }
}
