using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Recovery;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class RecoveryViewModel : ObservableObject
{
    private readonly IRecoveryCentreService _recovery;
    private readonly IProjectDialogService _dialogs;
    private readonly IProjectService _projects;
    private CancellationTokenSource? _operationCts;

    public RecoveryViewModel(
        IRecoveryCentreService recovery,
        IProjectDialogService dialogs,
        IProjectService projects)
    {
        _recovery = recovery;
        _dialogs = dialogs;
        _projects = projects;
    }

    public ObservableCollection<string> DiagnosticNotes { get; } = [];

    public ObservableCollection<string> MissingChapters { get; } = [];

    public ObservableCollection<RecoverySnapshotItem> ActiveSnapshots { get; } = [];

    public ObservableCollection<RecoverySnapshotItem> RetiredSnapshots { get; } = [];

    public ObservableCollection<RecoveryJournalInfo> JournalEntries { get; } = [];

    [ObservableProperty] private string _selectedRootPath = string.Empty;
    [ObservableProperty] private string _projectTitle = string.Empty;
    [ObservableProperty] private string _integritySummary = "No project selected.";
    [ObservableProperty] private string _integrityCheckedUtc = string.Empty;
    [ObservableProperty] private bool _databaseExists;
    [ObservableProperty] private bool _metadataExists;
    [ObservableProperty] private bool _databaseReadable;
    [ObservableProperty] private bool _integrityHealthy;
    [ObservableProperty] private string _statusMessage = "Select a project folder to diagnose.";
    [ObservableProperty] private string _progressMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _snapshotPreview = string.Empty;
    [ObservableProperty] private string _journalPreview = string.Empty;
    [ObservableProperty] private RecoverySnapshotItem? _selectedActiveSnapshot;
    [ObservableProperty] private RecoverySnapshotItem? _selectedRetiredSnapshot;
    [ObservableProperty] private RecoveryJournalInfo? _selectedJournalEntry;

    public async Task LoadRootAsync(string projectRootPath)
    {
        SelectedRootPath = Path.GetFullPath(projectRootPath);
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = _dialogs.PickFolder("Select a book project folder for recovery diagnostics");
        if (folder is null)
        {
            return;
        }

        await LoadRootAsync(folder).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task UseActiveProjectAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            StatusMessage = "No active project is open.";
            return;
        }

        await LoadRootAsync(project.RootPath).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedRootPath))
        {
            StatusMessage = "Select a project folder to diagnose.";
            return;
        }

        await RunBusyAsync("Refreshing diagnostics…", async (progress, token) =>
        {
            await ReloadReportAsync(progress, token).ConfigureAwait(true);
            StatusMessage = "Diagnostics refreshed.";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _operationCts?.Cancel();
        ProgressMessage = "Cancellation requested…";
    }

    [RelayCommand]
    private async Task PreviewActiveSnapshotAsync()
    {
        if (SelectedActiveSnapshot is null || string.IsNullOrWhiteSpace(SelectedRootPath))
        {
            return;
        }

        await PreviewSnapshotCoreAsync(SelectedActiveSnapshot.DirectoryPath).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PreviewRetiredSnapshotAsync()
    {
        if (SelectedRetiredSnapshot is null || string.IsNullOrWhiteSpace(SelectedRootPath))
        {
            return;
        }

        await PreviewSnapshotCoreAsync(SelectedRetiredSnapshot.DirectoryPath).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestoreActiveSnapshotAsync()
    {
        if (SelectedActiveSnapshot is null)
        {
            return;
        }

        await RestoreSnapshotCoreAsync(SelectedActiveSnapshot).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RestoreRetiredSnapshotAsync()
    {
        if (SelectedRetiredSnapshot is null)
        {
            return;
        }

        await RestoreSnapshotCoreAsync(SelectedRetiredSnapshot).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task UnretireSnapshotAsync()
    {
        if (SelectedRetiredSnapshot is null || string.IsNullOrWhiteSpace(SelectedRootPath))
        {
            return;
        }

        if (!_dialogs.Confirm(
                $"Move retired snapshot '{SelectedRetiredSnapshot.Name}' back to the active list?",
                "Unretire Snapshot"))
        {
            StatusMessage = "Unretire cancelled.";
            return;
        }

        await RunBusyAsync("Moving retired snapshot…", async (_, token) =>
        {
            var result = await _recovery
                .UnretireSnapshotAsync(SelectedRootPath, SelectedRetiredSnapshot.DirectoryPath, token)
                .ConfigureAwait(true);
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                await ReloadReportAsync(progress: null, token).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PreviewJournalAsync()
    {
        if (SelectedJournalEntry is null || string.IsNullOrWhiteSpace(SelectedRootPath))
        {
            return;
        }

        await RunBusyAsync("Loading journal preview…", async (_, token) =>
        {
            var preview = await _recovery
                .PreviewJournalAsync(SelectedRootPath, SelectedJournalEntry.EntryId, token)
                .ConfigureAwait(true);
            if (preview is null)
            {
                JournalPreview = string.Empty;
                StatusMessage = "Journal entry was not found.";
                return;
            }

            JournalPreview = preview.DraftText;
            StatusMessage = $"Journal preview loaded ({preview.CharacterCount} characters).";
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RecoverJournalCopyAsync()
    {
        if (SelectedJournalEntry is null || string.IsNullOrWhiteSpace(SelectedRootPath))
        {
            return;
        }

        if (!_dialogs.Confirm(
                "Write this journal draft to a separate recovered copy without changing the current project content?",
                "Recover Journal Copy"))
        {
            StatusMessage = "Recovered copy cancelled.";
            return;
        }

        await RunBusyAsync("Writing recovered copy…", async (progress, token) =>
        {
            var result = await _recovery
                .RecoverJournalToCopyAsync(SelectedRootPath, SelectedJournalEntry.EntryId, token, progress)
                .ConfigureAwait(true);
            StatusMessage = result.Message;
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RecoverJournalOverwriteAsync()
    {
        if (SelectedJournalEntry is null || string.IsNullOrWhiteSpace(SelectedRootPath))
        {
            return;
        }

        var first = _dialogs.Confirm(
            "Overwrite the current project content with this journal draft? A protection snapshot will be created first.",
            "Confirm Journal Overwrite");
        if (!first)
        {
            StatusMessage = "Overwrite cancelled.";
            return;
        }

        var second = _dialogs.Confirm(
            "Final confirmation: overwrite current content with the journal draft?",
            "Final Overwrite Confirmation");
        if (!second)
        {
            StatusMessage = "Overwrite cancelled.";
            return;
        }

        await RunBusyAsync("Overwriting from journal…", async (progress, token) =>
        {
            var result = await _recovery.RecoverJournalOverwriteAsync(
                    SelectedRootPath,
                    SelectedJournalEntry.EntryId,
                    firstConfirmation: true,
                    secondConfirmation: true,
                    token,
                    progress)
                .ConfigureAwait(true);
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                await ReloadReportAsync(progress: null, token).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RebuildMetadataAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedRootPath))
        {
            return;
        }

        if (!_dialogs.Confirm(
                "Rebuild missing project.json from a healthy database?",
                "Rebuild Metadata"))
        {
            StatusMessage = "Metadata rebuild cancelled.";
            return;
        }

        await RunBusyAsync("Rebuilding metadata…", async (progress, token) =>
        {
            var result = await _recovery.RebuildMetadataAsync(SelectedRootPath, token, progress)
                .ConfigureAwait(true);
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                await ReloadReportAsync(progress: null, token).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    private async Task PreviewSnapshotCoreAsync(string directoryPath)
    {
        await RunBusyAsync("Previewing snapshot…", async (_, token) =>
        {
            var preview = await _recovery
                .PreviewSnapshotAsync(SelectedRootPath, directoryPath, token)
                .ConfigureAwait(true);
            SnapshotPreview = preview.CanRestore
                ? $"Snapshot '{preview.SnapshotName}' · {preview.FileCount} files · project {preview.ProjectTitle}"
                : $"Invalid snapshot '{preview.SnapshotName}': {string.Join("; ", preview.ValidationErrors)}";
            StatusMessage = preview.CanRestore
                ? "Snapshot preview ready."
                : "Snapshot preview found validation errors.";
        }).ConfigureAwait(true);
    }

    private async Task RestoreSnapshotCoreAsync(RecoverySnapshotItem snapshot)
    {
        if (string.IsNullOrWhiteSpace(SelectedRootPath))
        {
            return;
        }

        if (!snapshot.IsValid)
        {
            StatusMessage = "Cannot restore an invalid snapshot.";
            return;
        }

        if (!_dialogs.Confirm(
                $"Restore snapshot '{snapshot.Name}'? A protection snapshot will be created first. This replaces current project content.",
                "Confirm Snapshot Restore"))
        {
            StatusMessage = "Restore cancelled.";
            return;
        }

        await RunBusyAsync("Restoring snapshot…", async (progress, token) =>
        {
            var result = await _recovery
                .RestoreSnapshotAsync(SelectedRootPath, snapshot.DirectoryPath, confirmed: true, token, progress)
                .ConfigureAwait(true);
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                await ReloadReportAsync(progress: null, token).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    private async Task ReloadReportAsync(
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        var report = await _recovery.DiagnoseAsync(SelectedRootPath, token, progress)
            .ConfigureAwait(true);
        ApplyReport(report);
    }

    private void ApplyReport(RecoveryCentreReport report)
    {
        SelectedRootPath = report.ProjectRootPath;
        ProjectTitle = report.ProjectTitle ?? "(unknown title)";
        DatabaseExists = report.DatabaseExists;
        MetadataExists = report.MetadataExists;
        DatabaseReadable = report.DatabaseReadable;
        IntegrityHealthy = report.Integrity.IsHealthy;
        IntegritySummary = report.Integrity.Summary;
        IntegrityCheckedUtc = report.Integrity.CheckedUtc.ToString("u");

        DiagnosticNotes.Clear();
        foreach (var note in report.DiagnosticNotes)
        {
            DiagnosticNotes.Add(note);
        }

        MissingChapters.Clear();
        foreach (var path in report.MissingChapterRelativePaths)
        {
            MissingChapters.Add(path);
        }

        ActiveSnapshots.Clear();
        foreach (var item in report.ActiveSnapshots)
        {
            ActiveSnapshots.Add(item);
        }

        RetiredSnapshots.Clear();
        foreach (var item in report.RetiredSnapshots)
        {
            RetiredSnapshots.Add(item);
        }

        JournalEntries.Clear();
        foreach (var item in report.JournalEntries)
        {
            JournalEntries.Add(item);
        }

        SelectedActiveSnapshot = ActiveSnapshots.FirstOrDefault();
        SelectedRetiredSnapshot = RetiredSnapshots.FirstOrDefault();
        SelectedJournalEntry = JournalEntries.FirstOrDefault();
        SnapshotPreview = string.Empty;
        JournalPreview = string.Empty;
    }

    private async Task RunBusyAsync(
        string progressText,
        Func<IProgress<OperationProgress>, CancellationToken, Task> action)
    {
        if (IsBusy)
        {
            StatusMessage = "Another recovery operation is already running.";
            return;
        }

        _operationCts?.Cancel();
        _operationCts = new CancellationTokenSource();
        var token = _operationCts.Token;
        IsBusy = true;
        ProgressMessage = progressText;
        try
        {
            var progress = new Progress<OperationProgress>(p => ProgressMessage = p.Message);
            await action(progress, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message.Length <= 240 ? ex.Message : ex.Message[..237] + "...";
        }
        finally
        {
            IsBusy = false;
            ProgressMessage = string.Empty;
        }
    }
}
