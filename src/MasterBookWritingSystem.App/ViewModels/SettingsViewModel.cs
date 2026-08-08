using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Navigation;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Settings;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IProjectService _projects;
    private readonly IExportService _exports;
    private readonly ISnapshotService _snapshots;
    private readonly IPortablePackageService _packages;
    private readonly IProjectDialogService _dialogs;
    private readonly IApplicationSettingsStore _settingsStore;
    private readonly INavigationService _navigation;

    public SettingsViewModel(
        IProjectService projects,
        IExportService exports,
        ISnapshotService snapshots,
        IPortablePackageService packages,
        IProjectDialogService dialogs,
        IApplicationSettingsStore settingsStore,
        INavigationService navigation)
    {
        _projects = projects;
        _exports = exports;
        _snapshots = snapshots;
        _packages = packages;
        _dialogs = dialogs;
        _settingsStore = settingsStore;
        _navigation = navigation;
        LoadRetentionSettings();
        _ = RefreshAsync();
    }

    public ObservableCollection<SnapshotListItemViewModel> Snapshots { get; } = [];

    [ObservableProperty] private bool _hasProject;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _progressMessage = string.Empty;
    [ObservableProperty] private SnapshotListItemViewModel? _selectedSnapshot;
    [ObservableProperty] private string _restorePreview = string.Empty;
    [ObservableProperty] private string _snapshotRetentionLimitText = SnapshotRetentionPolicy.DefaultMaxActiveSnapshots.ToString();
    [ObservableProperty] private string _snapshotRetentionExplanation =
        $"Active snapshot retention limit: {SnapshotRetentionPolicy.DefaultMaxActiveSnapshots}. "
        + "When the limit is exceeded, older snapshots are moved to 13 Archive/Snapshots/Retired/ "
        + "(recoverable) and are hidden from the active snapshot list. They are not permanently deleted.";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        LoadRetentionSettings();
        var project = _projects.ActiveProject;
        HasProject = project is not null;
        Snapshots.Clear();
        RestorePreview = string.Empty;
        if (project is null)
        {
            StatusMessage = "Open or create a project to use exports and backups.";
            return;
        }

        try
        {
            foreach (var snapshot in await _snapshots.ListSnapshotsAsync(project.Id).ConfigureAwait(true))
            {
                Snapshots.Add(new SnapshotListItemViewModel(snapshot));
            }

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenRecoveryCentre()
    {
        var root = _projects.ActiveProject?.RootPath;
        _navigation.NavigateToRecovery(root);
    }

    [RelayCommand]
    private async Task SaveSnapshotRetentionAsync()
    {
        if (!int.TryParse(SnapshotRetentionLimitText.Trim(), out var requested))
        {
            StatusMessage =
                $"Enter a whole number between {SnapshotRetentionPolicy.MinimumMaxActiveSnapshots} and {SnapshotRetentionPolicy.MaximumMaxActiveSnapshots}.";
            return;
        }

        if (!SnapshotRetentionPolicy.TryNormalize(requested, out var normalized, out var error))
        {
            StatusMessage = error
                ?? $"Enter a whole number between {SnapshotRetentionPolicy.MinimumMaxActiveSnapshots} and {SnapshotRetentionPolicy.MaximumMaxActiveSnapshots}.";
            return;
        }

        try
        {
            await _settingsStore.SaveAsync(new ApplicationSettings
            {
                SnapshotRetentionLimit = normalized,
            }).ConfigureAwait(true);
            SnapshotRetentionLimitText = normalized.ToString();
            UpdateRetentionExplanation(normalized);
            StatusMessage = $"Snapshot retention saved: keep {normalized} active snapshot(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void LoadRetentionSettings()
    {
        var settings = _settingsStore.GetSettings();
        SnapshotRetentionLimitText = settings.SnapshotRetentionLimit.ToString();
        UpdateRetentionExplanation(settings.SnapshotRetentionLimit);
    }

    private void UpdateRetentionExplanation(int limit)
    {
        SnapshotRetentionExplanation =
            $"Active snapshot retention limit: {limit}. "
            + "When the limit is exceeded, older snapshots are moved to 13 Archive/Snapshots/Retired/ "
            + "(recoverable) and are hidden from the active snapshot list. They are not permanently deleted.";
    }

    [RelayCommand]
    private async Task ExportDocxAsync()
    {
        await RunExport(async (projectId, progress) =>
            await _exports.ExportManuscriptDocxAsync(projectId, null, CancellationToken.None, progress)
                .ConfigureAwait(true));
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        await RunExport(async (projectId, progress) =>
            await _exports.ExportProjectJsonAsync(projectId, CancellationToken.None, progress).ConfigureAwait(true));
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        await RunExport(async (projectId, progress) =>
            await _exports.ExportStoryDataCsvAsync(projectId, CancellationToken.None, progress).ConfigureAwait(true));
    }

    [RelayCommand]
    private async Task ExportPortableZipAsync()
    {
        await RunExport(async (projectId, progress) =>
            await _packages.ExportZipAsync(projectId, CancellationToken.None, progress).ConfigureAwait(true));
    }

    [RelayCommand]
    private async Task CreateSnapshotAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var progress = new Progress<OperationProgress>(p => ProgressMessage = p.Message);
            var snapshot = await _snapshots.CreateSnapshotAsync(project.Id, CancellationToken.None, progress)
                .ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            StatusMessage = $"Snapshot created: {snapshot.Name} ({snapshot.Manifest.Files.Count} files).";
            ProgressMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            ProgressMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task InspectSnapshotAsync()
    {
        var project = _projects.ActiveProject;
        var selected = SelectedSnapshot;
        if (project is null || selected is null)
        {
            return;
        }

        try
        {
            var preview = await _snapshots
                .PreviewRestoreAsync(project.Id, selected.DirectoryPath)
                .ConfigureAwait(true);
            RestorePreview = preview.CanRestore
                ? $"Will restore {preview.FileCount} files for '{preview.ProjectTitle}' ({preview.ProjectId}).\n"
                  + string.Join("\n", preview.IncludedRelativePaths.Take(40))
                  + (preview.IncludedRelativePaths.Count > 40 ? "\n…" : string.Empty)
                : "Validation failed:\n" + string.Join("\n", preview.ValidationErrors);
            StatusMessage = preview.CanRestore ? "Snapshot is valid." : "Snapshot is invalid.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RestoreSnapshotAsync()
    {
        var project = _projects.ActiveProject;
        var selected = SelectedSnapshot;
        if (project is null || selected is null)
        {
            return;
        }

        var preview = await _snapshots.PreviewRestoreAsync(project.Id, selected.DirectoryPath).ConfigureAwait(true);
        RestorePreview = preview.CanRestore
            ? $"Will restore {preview.FileCount} files for '{preview.ProjectTitle}'.\n"
              + string.Join("\n", preview.IncludedRelativePaths.Take(40))
            : string.Join("\n", preview.ValidationErrors);

        if (!preview.CanRestore)
        {
            StatusMessage = "Cannot restore an invalid snapshot.";
            return;
        }

        if (!_dialogs.Confirm(
                $"Restore snapshot '{selected.Name}' into the current project?\n\nThis replaces restorable project files after validation.",
                "Confirm Restore"))
        {
            StatusMessage = "Restore cancelled.";
            return;
        }

        try
        {
            var progress = new Progress<OperationProgress>(p => ProgressMessage = p.Message);
            var result = await _snapshots
                .RestoreSnapshotAsync(project.Id, selected.DirectoryPath, confirmed: true, CancellationToken.None, progress)
                .ConfigureAwait(true);
            StatusMessage = result.Message;
            ProgressMessage = string.Empty;
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            ProgressMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ImportPortableZipAsync()
    {
        var zip = _dialogs.PickOpenFile("Import portable project ZIP", "ZIP packages|*.zip");
        if (zip is null)
        {
            return;
        }

        var destination = _dialogs.PickFolder("Choose a new folder for the imported project");
        if (destination is null)
        {
            return;
        }

        var target = Path.Combine(destination, Path.GetFileNameWithoutExtension(zip));
        var exists = Directory.Exists(target)
            && Directory.EnumerateFileSystemEntries(target).Any();
        if (exists
            && !_dialogs.Confirm(
                $"Destination already contains files:\n{target}\n\nOverwrite?",
                "Confirm Overwrite"))
        {
            StatusMessage = "Import cancelled.";
            return;
        }

        try
        {
            var progress = new Progress<OperationProgress>(p => ProgressMessage = p.Message);
            await _packages.ImportZipAsync(zip, target, overwriteExisting: exists, CancellationToken.None, progress)
                .ConfigureAwait(true);
            StatusMessage = $"Imported portable project to {target}.";
            ProgressMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            ProgressMessage = string.Empty;
        }
    }

    private async Task RunExport(Func<Guid, IProgress<OperationProgress>, Task<ExportResult>> action)
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var progress = new Progress<OperationProgress>(p => ProgressMessage = p.Message);
            var result = await action(project.Id, progress).ConfigureAwait(true);
            StatusMessage = $"{result.Format} export written to {result.RelativePath}.";
            ProgressMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            ProgressMessage = string.Empty;
        }
    }
}

public sealed class SnapshotListItemViewModel(SnapshotInfo snapshot)
{
    public string Name { get; } = snapshot.Name;

    public string DirectoryPath { get; } = snapshot.DirectoryPath;

    public string Kind { get; } = snapshot.Manifest.Kind switch
    {
        SnapshotKind.Automatic => "Automatic",
        SnapshotKind.Safety => "Safety",
        _ => "Manual",
    };

    public string DisplayWithKind => snapshot.IsValid
        ? $"{Kind}: {snapshot.Name} | {snapshot.Manifest.Files.Count} files | {snapshot.Manifest.CreatedUtc:u}"
        : $"{Kind}: {snapshot.Name} | INVALID";

    public string Display => snapshot.IsValid
        ? $"{snapshot.Name} · {snapshot.Manifest.Files.Count} files · {snapshot.Manifest.CreatedUtc:u}"
        : $"{snapshot.Name} · INVALID";
}
