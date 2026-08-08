using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Navigation;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Accessibility;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Recovery;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IProjectService _projectService;
    private readonly IProjectDialogService _dialogs;
    private readonly IRecentProjectsStore _recentProjects;
    private readonly ISnapshotService _snapshots;
    private readonly IEditorAutosaveService _autosave;
    private readonly ISaveStateService _saveState;
    private readonly IDraftingTimerService _timer;

    public ShellViewModel(
        INavigationService navigationService,
        IProjectService projectService,
        IProjectDialogService dialogs,
        IRecentProjectsStore recentProjects,
        ISnapshotService snapshots,
        IEditorAutosaveService autosave,
        ISaveStateService saveState,
        IDraftingTimerService timer)
    {
        _navigationService = navigationService;
        _projectService = projectService;
        _dialogs = dialogs;
        _recentProjects = recentProjects;
        _snapshots = snapshots;
        _autosave = autosave;
        _saveState = saveState;
        _timer = timer;
        _navigationService.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(INavigationService.CurrentViewModel)
                or nameof(INavigationService.CurrentSection)
                or null)
            {
                OnPropertyChanged(nameof(CurrentViewModel));
                OnPropertyChanged(nameof(CurrentSection));
            }
        };
        _saveState.Changed += (_, _) => SyncSaveState();
        _timer.Changed += (_, _) => SyncTimerDisplay();
        UpdateProjectCaption();
        SyncSaveState();
        SyncTimerDisplay();
    }

    public ObservableObject CurrentViewModel => _navigationService.CurrentViewModel;

    public AppSection CurrentSection => _navigationService.CurrentSection;

    public string ApplicationTitle => "Master Book-Writing System";

    [ObservableProperty]
    private string _activeProjectCaption = "No project open";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _saveStateDisplay = "Clean";

    [ObservableProperty]
    private string _saveStateAccessibleName = "Save status: Clean";

    [ObservableProperty]
    private string _timerDisplay = "Timer: stopped";

    [RelayCommand]
    private void Navigate(AppSection section) => _navigationService.NavigateTo(section);

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        try
        {
            await FlushPendingSavesAsync().ConfigureAwait(true);
            var parent = _dialogs.PickFolder("Choose a folder for the new portable book project");
            if (parent is null)
            {
                return;
            }

            var details = _dialogs.PromptCreateProject();
            if (details is null)
            {
                return;
            }

            var project = await _projectService.CreateAsync(new CreateProjectRequest
            {
                ParentDirectory = parent,
                Title = details.Title,
                Author = details.Author,
                Genre = details.Genre,
                NorthStar = details.NorthStar,
            }).ConfigureAwait(true);

            await _recentProjects.AddAsync(project.RootPath).ConfigureAwait(true);
            var protectionStatus = await ProtectProjectAsync(project.Id).ConfigureAwait(true);
            await _saveState.RefreshRecoveryAvailabilityAsync(project.Id).ConfigureAwait(true);
            await _timer.OnProjectOpenedAsync(project.Id).ConfigureAwait(true);
            UpdateProjectCaption();
            SyncSaveState();
            SyncTimerDisplay();
            StatusMessage = $"Created {project.Title}.{protectionStatus}";
            _navigationService.NavigateTo(AppSection.Dashboard);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _dialogs.ShowMessage(ex.Message, "Create Project Failed");
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        string? folder = null;
        try
        {
            await FlushPendingSavesAsync().ConfigureAwait(true);
            folder = _dialogs.PickFolder("Select an existing book project folder (contains project.mbws)");
            if (folder is null)
            {
                return;
            }

            var project = await _projectService.OpenAsync(folder).ConfigureAwait(true);
            await _recentProjects.AddAsync(project.RootPath).ConfigureAwait(true);
            var protectionStatus = await ProtectProjectAsync(project.Id).ConfigureAwait(true);
            await _saveState.RefreshRecoveryAvailabilityAsync(project.Id).ConfigureAwait(true);
            await _timer.OnProjectOpenedAsync(project.Id).ConfigureAwait(true);
            UpdateProjectCaption();
            SyncSaveState();
            SyncTimerDisplay();
            var interruptNote = _timer.GetSnapshot().HasInterruptedSession
                ? " Interrupted drafting session available under Progress."
                : string.Empty;
            StatusMessage = $"Opened {project.Title}.{protectionStatus}{interruptNote}";
            _navigationService.NavigateTo(AppSection.Dashboard);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            var openRecovery = folder is not null
                && _dialogs.Confirm(
                    $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Open this folder in the Recovery Centre?",
                    "Open Project Failed");
            if (openRecovery)
            {
                _navigationService.NavigateToRecovery(folder);
            }
            else
            {
                _dialogs.ShowMessage(ex.Message, "Open Project Failed");
            }
        }
    }

    [RelayCommand]
    private async Task CloseProjectAsync()
    {
        await FlushPendingSavesAsync().ConfigureAwait(true);
        _autosave.CancelAll();
        await _timer.OnProjectClosedAsync().ConfigureAwait(true);
        await _projectService.CloseAsync().ConfigureAwait(true);
        _saveState.Report(SaveState.Clean, "Clean");
        UpdateProjectCaption();
        SyncSaveState();
        SyncTimerDisplay();
        StatusMessage = "Project closed";
        _navigationService.NavigateTo(AppSection.Dashboard);
    }

    public async Task FlushPendingSavesAsync()
    {
        try
        {
            if (_autosave.HasPendingWork)
            {
                await _autosave.FlushAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _saveState.Report(SaveState.SaveFailed, ex.Message);
            SyncSaveState();
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    private async Task<string> ProtectProjectAsync(Guid projectId)
    {
        try
        {
            var result = await _snapshots
                .CreateAutomaticSnapshotIfNeededAsync(projectId)
                .ConfigureAwait(true);
            return result.Outcome == AutomaticSnapshotOutcome.Created
                ? " Automatic safety snapshot created"
                : string.Empty;
        }
        catch (Exception ex)
        {
            return $" Automatic snapshot warning: {ex.Message}";
        }
    }

    private void UpdateProjectCaption()
    {
        var project = _projectService.ActiveProject;
        ActiveProjectCaption = project is null
            ? "No project open"
            : $"{project.Title}  •  {project.RootPath}";
    }

    private void SyncSaveState()
    {
        SaveStateDisplay = SaveStateAccessibility.FormatDisplay(_saveState.State, _saveState.Message);
        SaveStateAccessibleName = SaveStateAccessibility.FormatAccessibleName(_saveState.State, _saveState.Message);
    }

    private void SyncTimerDisplay()
    {
        var snapshot = _timer.GetSnapshot();
        var elapsed = snapshot.ActiveElapsed;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var clock = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        TimerDisplay = snapshot.HasInterruptedSession
            ? $"Timer: interrupted ({clock})"
            : $"Timer: {snapshot.State.ToString().ToLowerInvariant()} ({clock})";
    }
}
