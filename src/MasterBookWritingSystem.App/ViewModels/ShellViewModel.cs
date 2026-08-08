using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Navigation;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IProjectService _projectService;
    private readonly IProjectDialogService _dialogs;
    private readonly IRecentProjectsStore _recentProjects;
    private readonly ISnapshotService _snapshots;

    public ShellViewModel(
        INavigationService navigationService,
        IProjectService projectService,
        IProjectDialogService dialogs,
        IRecentProjectsStore recentProjects,
        ISnapshotService snapshots)
    {
        _navigationService = navigationService;
        _projectService = projectService;
        _dialogs = dialogs;
        _recentProjects = recentProjects;
        _snapshots = snapshots;
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
        UpdateProjectCaption();
    }

    public ObservableObject CurrentViewModel => _navigationService.CurrentViewModel;

    public AppSection CurrentSection => _navigationService.CurrentSection;

    public string ApplicationTitle => "Master Book-Writing System";

    [ObservableProperty]
    private string _activeProjectCaption = "No project open";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private void Navigate(AppSection section) => _navigationService.NavigateTo(section);

    [RelayCommand]
    private async Task CreateProjectAsync()
    {
        try
        {
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
            UpdateProjectCaption();
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
        try
        {
            var folder = _dialogs.PickFolder("Select an existing book project folder (contains project.mbws)");
            if (folder is null)
            {
                return;
            }

            var project = await _projectService.OpenAsync(folder).ConfigureAwait(true);
            await _recentProjects.AddAsync(project.RootPath).ConfigureAwait(true);
            var protectionStatus = await ProtectProjectAsync(project.Id).ConfigureAwait(true);
            UpdateProjectCaption();
            StatusMessage = $"Opened {project.Title}.{protectionStatus}";
            _navigationService.NavigateTo(AppSection.Dashboard);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _dialogs.ShowMessage(ex.Message, "Open Project Failed");
        }
    }

    [RelayCommand]
    private async Task CloseProjectAsync()
    {
        await _projectService.CloseAsync().ConfigureAwait(true);
        UpdateProjectCaption();
        StatusMessage = "Project closed";
        _navigationService.NavigateTo(AppSection.Dashboard);
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
}
