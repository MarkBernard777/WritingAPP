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
    private Guid? _pendingCharacterId;
    private Guid? _pendingWorldEntryId;
    private Guid? _pendingBeatId;
    private Guid? _pendingManuscriptChapterId;
    private Guid? _pendingManuscriptSceneId;
    private bool _pendingShowCorkboard;
    private string? _pendingRecoveryRoot;

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
        ClearStoryDataPending();
        _pendingSceneId = sceneId;
        NavigateTo(AppSection.StoryData);
    }

    public void NavigateToStoryDataCharacter(Guid characterId)
    {
        ClearStoryDataPending();
        _pendingCharacterId = characterId;
        NavigateTo(AppSection.StoryData);
    }

    public void NavigateToStoryDataWorldEntry(Guid worldEntryId)
    {
        ClearStoryDataPending();
        _pendingWorldEntryId = worldEntryId;
        NavigateTo(AppSection.StoryData);
    }

    public void NavigateToStoryDataBeat(Guid beatId)
    {
        ClearStoryDataPending();
        _pendingBeatId = beatId;
        NavigateTo(AppSection.StoryData);
    }

    public void NavigateToManuscriptChapter(Guid chapterId, Guid? sceneId = null)
    {
        _pendingManuscriptChapterId = chapterId;
        _pendingManuscriptSceneId = sceneId;
        _pendingShowCorkboard = false;
        NavigateTo(AppSection.Manuscript);
    }

    public void NavigateToManuscriptCorkboard(Guid? sceneId = null)
    {
        _pendingManuscriptChapterId = null;
        _pendingManuscriptSceneId = sceneId;
        _pendingShowCorkboard = true;
        NavigateTo(AppSection.Manuscript);
    }

    public void NavigateToRecovery(string? projectRootPath = null)
    {
        _pendingRecoveryRoot = projectRootPath;
        NavigateTo(AppSection.Recovery);
    }

    public void NavigateTo(AppSection section)
    {
        DetachCurrent();
        CurrentViewModel = section switch
        {
            AppSection.Dashboard => _services.GetRequiredService<DashboardViewModel>(),
            AppSection.Workflow => _services.GetRequiredService<WorkflowViewModel>(),
            AppSection.Documents => _services.GetRequiredService<DocumentsViewModel>(),
            AppSection.Manuscript => CreateManuscriptViewModel(),
            AppSection.StoryData => CreateStoryDataViewModel(),
            AppSection.Progress => _services.GetRequiredService<ProgressViewModel>(),
            AppSection.Tools => _services.GetRequiredService<ToolsViewModel>(),
            AppSection.Publishing => _services.GetRequiredService<PublishingViewModel>(),
            AppSection.Settings => _services.GetRequiredService<SettingsViewModel>(),
            AppSection.Recovery => CreateRecoveryViewModel(),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };
        CurrentSection = section;
    }

    private StoryDataViewModel CreateStoryDataViewModel()
    {
        var viewModel = _services.GetRequiredService<StoryDataViewModel>();
        if (_pendingSceneId is { } sceneId)
        {
            _pendingSceneId = null;
            _ = viewModel.FocusSceneAsync(sceneId);
        }
        else if (_pendingCharacterId is { } characterId)
        {
            _pendingCharacterId = null;
            _ = viewModel.FocusCharacterAsync(characterId);
        }
        else if (_pendingWorldEntryId is { } worldEntryId)
        {
            _pendingWorldEntryId = null;
            _ = viewModel.FocusWorldEntryAsync(worldEntryId);
        }
        else if (_pendingBeatId is { } beatId)
        {
            _pendingBeatId = null;
            _ = viewModel.FocusBeatAsync(beatId);
        }

        return viewModel;
    }

    private ManuscriptViewModel CreateManuscriptViewModel()
    {
        var viewModel = _services.GetRequiredService<ManuscriptViewModel>();
        if (_pendingShowCorkboard)
        {
            _pendingShowCorkboard = false;
            var sceneId = _pendingManuscriptSceneId;
            _pendingManuscriptSceneId = null;
            _pendingManuscriptChapterId = null;
            _ = viewModel.FocusCorkboardAsync(sceneId);
        }
        else if (_pendingManuscriptChapterId is { } chapterId)
        {
            var sceneId = _pendingManuscriptSceneId;
            _pendingManuscriptChapterId = null;
            _pendingManuscriptSceneId = null;
            _ = viewModel.FocusChapterAsync(chapterId, sceneId);
        }

        return viewModel;
    }

    private RecoveryViewModel CreateRecoveryViewModel()
    {
        var viewModel = _services.GetRequiredService<RecoveryViewModel>();
        var pending = _pendingRecoveryRoot;
        _pendingRecoveryRoot = null;
        if (!string.IsNullOrWhiteSpace(pending))
        {
            _ = viewModel.LoadRootAsync(pending);
        }

        return viewModel;
    }

    private void ClearStoryDataPending()
    {
        _pendingSceneId = null;
        _pendingCharacterId = null;
        _pendingWorldEntryId = null;
        _pendingBeatId = null;
    }

    /// <summary>
    /// Sole owner of section view-model lifecycle cleanup. Views must not call Detach on VMs.
    /// </summary>
    private void DetachCurrent()
    {
        if (_currentViewModel is ManuscriptViewModel manuscript)
        {
            manuscript.Detach();
        }
        else if (_currentViewModel is StoryDataViewModel storyData)
        {
            storyData.Detach();
        }
        else if (_currentViewModel is DocumentsViewModel documents)
        {
            documents.Detach();
        }
    }
}
