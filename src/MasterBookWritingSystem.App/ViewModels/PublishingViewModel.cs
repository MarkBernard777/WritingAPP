using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Publishing;
using MasterBookWritingSystem.Core.Publishing;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class PublishingViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IPublishingService _publishing;
    private readonly IProjectDialogService _dialogs;

    public PublishingViewModel(
        IProjectService projectService,
        IPublishingService publishing,
        IProjectDialogService dialogs)
    {
        _projectService = projectService;
        _publishing = publishing;
        _dialogs = dialogs;
        _ = RefreshAsync();
    }

    public ObservableCollection<SubmissionListItemViewModel> Submissions { get; } = [];

    public ObservableCollection<LaunchItemListItemViewModel> LaunchItems { get; } = [];

    public ObservableCollection<RightsContractListItemViewModel> RightsContracts { get; } = [];

    public ObservableCollection<PublishingFormatListItemViewModel> Formats { get; } = [];

    public ObservableCollection<MetadataRecordListItemViewModel> MetadataRecords { get; } = [];

    public ObservableCollection<PerformanceRecordListItemViewModel> PerformanceRecords { get; } = [];

    public ObservableCollection<CorrectionListItemViewModel> Corrections { get; } = [];

    public IReadOnlyList<SubmissionResponse> ResponseOptions { get; } = Enum.GetValues<SubmissionResponse>();

    public IReadOnlyList<SubmissionOutcome> OutcomeOptions { get; } = Enum.GetValues<SubmissionOutcome>();

    public IReadOnlyList<LaunchItemStatus> LaunchStatusOptions { get; } = Enum.GetValues<LaunchItemStatus>();

    public IReadOnlyList<RightsContractStatus> RightsStatusOptions { get; } = Enum.GetValues<RightsContractStatus>();

    public IReadOnlyList<PublishingFormatKind> FormatKindOptions { get; } = Enum.GetValues<PublishingFormatKind>();

    public IReadOnlyList<PublicationStatus> PublicationStatusOptions { get; } = Enum.GetValues<PublicationStatus>();

    public IReadOnlyList<CorrectionStatus> CorrectionStatusOptions { get; } = Enum.GetValues<CorrectionStatus>();

    public IReadOnlyList<PublishingRoute> RouteOptions { get; } =
    [
        PublishingRoute.Traditional,
        PublishingRoute.SelfPublishing,
        PublishingRoute.Hybrid,
    ];

    public IReadOnlyList<PublishingRoute> LaunchRouteFilterOptions { get; } =
    [
        PublishingRoute.Unspecified,
        PublishingRoute.Traditional,
        PublishingRoute.SelfPublishing,
        PublishingRoute.Hybrid,
    ];

    public IReadOnlyList<string> OutcomeFilterOptions { get; } =
        ["(All outcomes)", .. Enum.GetNames<SubmissionOutcome>()];

    public IReadOnlyList<string> LaunchStatusFilterOptions { get; } =
        ["(All statuses)", .. Enum.GetNames<LaunchItemStatus>()];

    public IReadOnlyList<string> RightsStatusFilterOptions { get; } =
        ["(All statuses)", .. Enum.GetNames<RightsContractStatus>()];

    public IReadOnlyList<string> PublicationStatusFilterOptions { get; } =
        ["(All statuses)", .. Enum.GetNames<PublicationStatus>()];

    public IReadOnlyList<string> CorrectionStatusFilterOptions { get; } =
        ["(All statuses)", .. Enum.GetNames<CorrectionStatus>()];

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _activeRouteLabel = "No project open";

    [ObservableProperty]
    private string _submissionsRouteNote = string.Empty;

    [ObservableProperty]
    private string _selectedOutcomeFilter = "(All outcomes)";

    [ObservableProperty]
    private string _selectedLaunchStatusFilter = "(All statuses)";

    [ObservableProperty]
    private PublishingRoute _selectedLaunchRouteFilter = PublishingRoute.Unspecified;

    [ObservableProperty]
    private bool _launchSortDescending;

    [ObservableProperty]
    private string _selectedRightsStatusFilter = "(All statuses)";

    [ObservableProperty]
    private string _selectedFormatStatusFilter = "(All statuses)";

    [ObservableProperty]
    private string _selectedMetadataStatusFilter = "(All statuses)";

    [ObservableProperty]
    private string _performanceFormatFilter = string.Empty;

    [ObservableProperty]
    private bool _performanceSortDescending = true;

    [ObservableProperty]
    private string _selectedCorrectionStatusFilter = "(All statuses)";

    [ObservableProperty]
    private bool _correctionSortDescending = true;

    [ObservableProperty]
    private bool _showInactiveSubmissions;

    [ObservableProperty]
    private SubmissionListItemViewModel? _selectedSubmission;

    [ObservableProperty]
    private LaunchItemListItemViewModel? _selectedLaunchItem;

    [ObservableProperty]
    private RightsContractListItemViewModel? _selectedRightsContract;

    [ObservableProperty]
    private PublishingFormatListItemViewModel? _selectedFormat;

    [ObservableProperty]
    private MetadataRecordListItemViewModel? _selectedMetadataRecord;

    [ObservableProperty]
    private PerformanceRecordListItemViewModel? _selectedPerformanceRecord;

    [ObservableProperty]
    private CorrectionListItemViewModel? _selectedCorrection;

    [ObservableProperty]
    private SubmissionEditorViewModel? _submissionEditor;

    [ObservableProperty]
    private LaunchItemEditorViewModel? _launchEditor;

    [ObservableProperty]
    private RightsContractEditorViewModel? _rightsEditor;

    [ObservableProperty]
    private PublishingFormatEditorViewModel? _formatEditor;

    [ObservableProperty]
    private MetadataRecordEditorViewModel? _metadataEditor;

    [ObservableProperty]
    private PerformanceRecordEditorViewModel? _performanceEditor;

    [ObservableProperty]
    private CorrectionEditorViewModel? _correctionEditor;

    partial void OnSelectedSubmissionChanged(SubmissionListItemViewModel? value)
        => SubmissionEditor = value is null ? null : SubmissionEditorViewModel.From(value.Source);

    partial void OnSelectedLaunchItemChanged(LaunchItemListItemViewModel? value)
        => LaunchEditor = value is null ? null : LaunchItemEditorViewModel.From(value.Source);

    partial void OnSelectedRightsContractChanged(RightsContractListItemViewModel? value)
        => RightsEditor = value is null ? null : RightsContractEditorViewModel.From(value.Source);

    partial void OnSelectedFormatChanged(PublishingFormatListItemViewModel? value)
        => FormatEditor = value is null ? null : PublishingFormatEditorViewModel.From(value.Source);

    partial void OnSelectedMetadataRecordChanged(MetadataRecordListItemViewModel? value)
        => MetadataEditor = value is null ? null : MetadataRecordEditorViewModel.From(value.Source);

    partial void OnSelectedPerformanceRecordChanged(PerformanceRecordListItemViewModel? value)
        => PerformanceEditor = value is null ? null : PerformanceRecordEditorViewModel.From(value.Source);

    partial void OnSelectedCorrectionChanged(CorrectionListItemViewModel? value)
        => CorrectionEditor = value is null ? null : CorrectionEditorViewModel.From(value.Source);

    partial void OnSelectedOutcomeFilterChanged(string value) => _ = RefreshSubmissionsAsync();

    partial void OnShowInactiveSubmissionsChanged(bool value) => _ = RefreshSubmissionsAsync();

    partial void OnSelectedLaunchStatusFilterChanged(string value) => _ = RefreshLaunchItemsAsync();

    partial void OnSelectedLaunchRouteFilterChanged(PublishingRoute value) => _ = RefreshLaunchItemsAsync();

    partial void OnLaunchSortDescendingChanged(bool value) => _ = RefreshLaunchItemsAsync();

    partial void OnSelectedRightsStatusFilterChanged(string value) => _ = RefreshRightsAsync();

    partial void OnSelectedFormatStatusFilterChanged(string value) => _ = RefreshFormatsAsync();

    partial void OnSelectedMetadataStatusFilterChanged(string value) => _ = RefreshMetadataAsync();

    partial void OnPerformanceFormatFilterChanged(string value) => _ = RefreshPerformanceAsync();

    partial void OnPerformanceSortDescendingChanged(bool value) => _ = RefreshPerformanceAsync();

    partial void OnSelectedCorrectionStatusFilterChanged(string value) => _ = RefreshCorrectionsAsync();

    partial void OnCorrectionSortDescendingChanged(bool value) => _ = RefreshCorrectionsAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var project = _projectService.ActiveProject;
        HasProject = project is not null;
        ClearLists();

        if (project is null)
        {
            ActiveRouteLabel = "No project open";
            SubmissionsRouteNote = string.Empty;
            StatusMessage = "Open or create a project to manage publishing workflows.";
            return;
        }

        ActiveRouteLabel = $"Active publishing route: {FormatRoute(project.PublishingRoute)}";
        SubmissionsRouteNote = project.PublishingRoute == PublishingRoute.SelfPublishing
            ? "Traditional submissions are hidden while Self-Publishing is active. Data is preserved and can be shown with Include inactive."
            : "Submissions for Traditional / Hybrid routes appear here. Switching routes never deletes records.";

        try
        {
            await RefreshSubmissionsAsync().ConfigureAwait(true);
            await RefreshLaunchItemsAsync().ConfigureAwait(true);
            await RefreshRightsAsync().ConfigureAwait(true);
            await RefreshFormatsAsync().ConfigureAwait(true);
            await RefreshMetadataAsync().ConfigureAwait(true);
            await RefreshPerformanceAsync().ConfigureAwait(true);
            await RefreshCorrectionsAsync().ConfigureAwait(true);
            StatusMessage =
                $"Loaded {Submissions.Count} submissions, {LaunchItems.Count} launch items, {RightsContracts.Count} rights/contracts, "
                + $"{Formats.Count} formats, {MetadataRecords.Count} metadata, {PerformanceRecords.Count} performance, {Corrections.Count} corrections.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddSubmissionAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var created = await _publishing.CreateSubmissionAsync(project.Id, new Submission
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "New submission",
                RouteAffinity = project.PublishingRoute == PublishingRoute.SelfPublishing
                    ? PublishingRoute.SelfPublishing
                    : PublishingRoute.Traditional,
            }).ConfigureAwait(true);
            await RefreshSubmissionsAsync().ConfigureAwait(true);
            SelectedSubmission = Submissions.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "Submission created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveSubmissionAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = SubmissionEditor;
        if (project is null || editor is null)
        {
            return;
        }

        var model = editor.ToModel();
        var validation = _publishing.ValidateSubmission(model);
        if (!validation.IsValid)
        {
            StatusMessage = string.Join(" ", validation.Errors);
            return;
        }

        try
        {
            var saved = await _publishing.UpdateSubmissionAsync(project.Id, model).ConfigureAwait(true);
            await RefreshSubmissionsAsync().ConfigureAwait(true);
            SelectedSubmission = Submissions.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = "Submission saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteSubmissionAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedSubmission;
        if (project is null || selected is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Delete submission '{selected.DisplayName}'?", "Delete Submission"))
        {
            return;
        }

        try
        {
            await _publishing.DeleteSubmissionAsync(project.Id, selected.Id, confirmed: true).ConfigureAwait(true);
            await RefreshSubmissionsAsync().ConfigureAwait(true);
            StatusMessage = "Submission deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddLaunchItemAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var created = await _publishing.CreateLaunchItemAsync(project.Id, new LaunchItem
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Asset = "New launch item",
                Date = DateOnly.FromDateTime(DateTime.Today),
                Status = LaunchItemStatus.Planned,
            }).ConfigureAwait(true);
            await RefreshLaunchItemsAsync().ConfigureAwait(true);
            SelectedLaunchItem = LaunchItems.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "Launch item created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveLaunchItemAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = LaunchEditor;
        if (project is null || editor is null)
        {
            return;
        }

        var model = editor.ToModel();
        var validation = _publishing.ValidateLaunchItem(model);
        if (!validation.IsValid)
        {
            StatusMessage = string.Join(" ", validation.Errors);
            return;
        }

        try
        {
            var saved = await _publishing.UpdateLaunchItemAsync(project.Id, model).ConfigureAwait(true);
            await RefreshLaunchItemsAsync().ConfigureAwait(true);
            SelectedLaunchItem = LaunchItems.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = "Launch item saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteLaunchItemAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedLaunchItem;
        if (project is null || selected is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Delete launch item '{selected.DisplayName}'?", "Delete Launch Item"))
        {
            return;
        }

        try
        {
            await _publishing.DeleteLaunchItemAsync(project.Id, selected.Id, confirmed: true).ConfigureAwait(true);
            await RefreshLaunchItemsAsync().ConfigureAwait(true);
            StatusMessage = "Launch item deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddRightsContractAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            var created = await _publishing.CreateRightsAndContractAsync(project.Id, new RightsAndContract
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Party = "New party",
                RightOrService = "Rights",
                Status = RightsContractStatus.Draft,
            }).ConfigureAwait(true);
            await RefreshRightsAsync().ConfigureAwait(true);
            SelectedRightsContract = RightsContracts.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "Rights/contract created.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveRightsContractAsync()
    {
        var project = _projectService.ActiveProject;
        var editor = RightsEditor;
        if (project is null || editor is null)
        {
            return;
        }

        var model = editor.ToModel();
        var validation = _publishing.ValidateRightsAndContract(model);
        if (!validation.IsValid)
        {
            StatusMessage = string.Join(" ", validation.Errors);
            return;
        }

        try
        {
            var saved = await _publishing.UpdateRightsAndContractAsync(project.Id, model).ConfigureAwait(true);
            await RefreshRightsAsync().ConfigureAwait(true);
            SelectedRightsContract = RightsContracts.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = "Rights/contract saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteRightsContractAsync()
    {
        var project = _projectService.ActiveProject;
        var selected = SelectedRightsContract;
        if (project is null || selected is null)
        {
            return;
        }

        if (!_dialogs.Confirm($"Delete rights/contract for '{selected.DisplayName}'?", "Delete Rights/Contract"))
        {
            return;
        }

        try
        {
            await _publishing.DeleteRightsAndContractAsync(project.Id, selected.Id, confirmed: true).ConfigureAwait(true);
            await RefreshRightsAsync().ConfigureAwait(true);
            StatusMessage = "Rights/contract deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenPublishingFolder()
        => OpenProjectRelative(ProjectPaths.PublishingDirectory, isDirectory: true);

    [RelayCommand]
    private void OpenMarketingFolder()
        => OpenProjectRelative(ProjectPaths.MarketingDirectory, isDirectory: true);

    [RelayCommand]
    private void OpenPublishingPlan()
        => OpenProjectRelative(ProjectPaths.PublishingPlanRelativePath, isDirectory: false);

    [RelayCommand]
    private void OpenLaunchPlan()
        => OpenProjectRelative(ProjectPaths.LaunchPlanRelativePath, isDirectory: false);

    [RelayCommand]
    private void OpenRightsRegister()
        => OpenProjectRelative(ProjectPaths.RightsContractsRegisterRelativePath, isDirectory: false);

    [RelayCommand]
    private void OpenMetadataSheet()
        => OpenProjectRelative(ProjectPaths.MetadataSheetRelativePath, isDirectory: false);

    [RelayCommand]
    private void OpenSubmissionLink()
    {
        if (SubmissionEditor is null)
        {
            return;
        }

        OpenProjectRelative(SubmissionEditor.Website, isDirectory: false);
    }

    [RelayCommand]
    private void OpenLaunchLink()
    {
        if (LaunchEditor is null)
        {
            return;
        }

        OpenProjectRelative(LaunchEditor.Link, isDirectory: false);
    }

    [RelayCommand]
    private void OpenAgreementFile()
    {
        if (RightsEditor is null)
        {
            return;
        }

        OpenProjectRelative(RightsEditor.AgreementFile, isDirectory: false);
    }

    private async Task RefreshSubmissionsAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        SubmissionOutcome? outcome = null;
        if (SelectedOutcomeFilter != "(All outcomes)"
            && Enum.TryParse<SubmissionOutcome>(SelectedOutcomeFilter, out var parsed))
        {
            outcome = parsed;
        }

        var previousId = SelectedSubmission?.Id;
        Submissions.Clear();
        SubmissionEditor = null;

        foreach (var item in await _publishing.GetSubmissionsAsync(
                     project.Id,
                     project.PublishingRoute,
                     outcome,
                     includeInactiveRouteRows: ShowInactiveSubmissions).ConfigureAwait(true))
        {
            var inactive = !PublishingValidation.IsSubmissionActiveForRoute(
                project.PublishingRoute,
                item.RouteAffinity);
            Submissions.Add(new SubmissionListItemViewModel(item, inactive));
        }

        SelectedSubmission = Submissions.FirstOrDefault(item => item.Id == previousId)
            ?? Submissions.FirstOrDefault();
    }

    private async Task RefreshLaunchItemsAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        LaunchItemStatus? status = null;
        if (SelectedLaunchStatusFilter != "(All statuses)"
            && Enum.TryParse<LaunchItemStatus>(SelectedLaunchStatusFilter, out var parsed))
        {
            status = parsed;
        }

        var previousId = SelectedLaunchItem?.Id;
        LaunchItems.Clear();
        LaunchEditor = null;

        foreach (var item in await _publishing.GetLaunchItemsAsync(
                     project.Id,
                     status,
                     SelectedLaunchRouteFilter,
                     LaunchSortDescending).ConfigureAwait(true))
        {
            LaunchItems.Add(new LaunchItemListItemViewModel(item));
        }

        SelectedLaunchItem = LaunchItems.FirstOrDefault(item => item.Id == previousId)
            ?? LaunchItems.FirstOrDefault();
    }

    private async Task RefreshRightsAsync()
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return;
        }

        RightsContractStatus? status = null;
        if (SelectedRightsStatusFilter != "(All statuses)"
            && Enum.TryParse<RightsContractStatus>(SelectedRightsStatusFilter, out var parsed))
        {
            status = parsed;
        }

        var previousId = SelectedRightsContract?.Id;
        RightsContracts.Clear();
        RightsEditor = null;

        foreach (var item in await _publishing.GetRightsAndContractsAsync(project.Id, status).ConfigureAwait(true))
        {
            RightsContracts.Add(new RightsContractListItemViewModel(item));
        }

        SelectedRightsContract = RightsContracts.FirstOrDefault(item => item.Id == previousId)
            ?? RightsContracts.FirstOrDefault();
    }

    private void ClearLists()
    {
        Submissions.Clear();
        LaunchItems.Clear();
        RightsContracts.Clear();
        Formats.Clear();
        MetadataRecords.Clear();
        PerformanceRecords.Clear();
        Corrections.Clear();
        SelectedSubmission = null;
        SelectedLaunchItem = null;
        SelectedRightsContract = null;
        SelectedFormat = null;
        SelectedMetadataRecord = null;
        SelectedPerformanceRecord = null;
        SelectedCorrection = null;
        SubmissionEditor = null;
        LaunchEditor = null;
        RightsEditor = null;
        FormatEditor = null;
        MetadataEditor = null;
        PerformanceEditor = null;
        CorrectionEditor = null;
    }

    private void OpenProjectRelative(string? relativePath, bool isDirectory)
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            StatusMessage = "Open a project first.";
            return;
        }

        var absolute = _publishing.ResolveLocalLink(project.RootPath, relativePath, out var missing);
        if (absolute is null)
        {
            StatusMessage = missing ?? "Linked path could not be opened.";
            _dialogs.ShowMessage(StatusMessage, "Missing linked file");
            return;
        }

        try
        {
            if (isDirectory || Directory.Exists(absolute))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = absolute,
                    UseShellExecute = true,
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = absolute,
                    UseShellExecute = true,
                });
            }

            StatusMessage = $"Opened {ProjectRelativePathRules.Normalize(relativePath ?? absolute)}";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _dialogs.ShowMessage(ex.Message, "Unable to open");
        }
    }

    private static string FormatRoute(PublishingRoute route) => route switch
    {
        PublishingRoute.SelfPublishing => "Self-Publishing",
        PublishingRoute.Traditional => "Traditional",
        PublishingRoute.Hybrid => "Hybrid",
        _ => "Unspecified",
    };
}

public sealed class SubmissionListItemViewModel(Submission source, bool isInactiveForRoute)
{
    public Submission Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => isInactiveForRoute
        ? $"{Source.Name} (inactive for current route)"
        : Source.Name;
}

public sealed class LaunchItemListItemViewModel(LaunchItem source)
{
    public LaunchItem Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => Source.Date is { } date
        ? $"{date:yyyy-MM-dd} — {Source.Asset}"
        : Source.Asset;
}

public sealed class RightsContractListItemViewModel(RightsAndContract source)
{
    public RightsAndContract Source { get; } = source;

    public Guid Id => Source.Id;

    public string DisplayName => $"{Source.Party} — {Source.RightOrService}";
}

public partial class SubmissionEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _agencyOrPublisher = string.Empty;

    [ObservableProperty]
    private string _website = string.Empty;

    [ObservableProperty]
    private string _fit = string.Empty;

    [ObservableProperty]
    private string _requirements = string.Empty;

    [ObservableProperty]
    private string _materialSent = string.Empty;

    [ObservableProperty]
    private string _sentDateText = string.Empty;

    [ObservableProperty]
    private string _followUpDateText = string.Empty;

    [ObservableProperty]
    private SubmissionResponse _response;

    [ObservableProperty]
    private SubmissionOutcome _outcome;

    [ObservableProperty]
    private PublishingRoute _routeAffinity = PublishingRoute.Traditional;

    public static SubmissionEditorViewModel From(Submission source) => new()
    {
        Id = source.Id,
        ProjectId = source.ProjectId,
        Name = source.Name,
        AgencyOrPublisher = source.AgencyOrPublisher,
        Website = source.Website,
        Fit = source.Fit,
        Requirements = source.Requirements,
        MaterialSent = source.MaterialSent,
        SentDateText = FormatDate(source.SentDate),
        FollowUpDateText = FormatDate(source.FollowUpDate),
        Response = source.Response,
        Outcome = source.Outcome,
        RouteAffinity = source.RouteAffinity,
    };

    public Submission ToModel() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        Name = Name,
        AgencyOrPublisher = AgencyOrPublisher,
        Website = Website,
        Fit = Fit,
        Requirements = Requirements,
        MaterialSent = MaterialSent,
        SentDate = ParseDate(SentDateText),
        FollowUpDate = ParseDate(FollowUpDateText),
        Response = Response,
        Outcome = Outcome,
        RouteAffinity = RouteAffinity,
    };

    private static string FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? string.Empty;

    private static DateOnly? ParseDate(string? text)
        => DateOnly.TryParse(text, out var value) ? value : null;
}

public partial class LaunchItemEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private string _phase = string.Empty;

    [ObservableProperty]
    private string _channel = string.Empty;

    [ObservableProperty]
    private string _asset = string.Empty;

    [ObservableProperty]
    private string _audience = string.Empty;

    [ObservableProperty]
    private string _owner = string.Empty;

    [ObservableProperty]
    private string _link = string.Empty;

    [ObservableProperty]
    private LaunchItemStatus _status;

    [ObservableProperty]
    private string _result = string.Empty;

    [ObservableProperty]
    private PublishingRoute _routeAffinity = PublishingRoute.Unspecified;

    public static LaunchItemEditorViewModel From(LaunchItem source) => new()
    {
        Id = source.Id,
        ProjectId = source.ProjectId,
        DateText = source.Date?.ToString("yyyy-MM-dd") ?? string.Empty,
        Phase = source.Phase,
        Channel = source.Channel,
        Asset = source.Asset,
        Audience = source.Audience,
        Owner = source.Owner,
        Link = source.Link,
        Status = source.Status,
        Result = source.Result,
        RouteAffinity = source.RouteAffinity,
    };

    public LaunchItem ToModel() => new()
    {
        Id = Id,
        ProjectId = ProjectId,
        Date = DateOnly.TryParse(DateText, out var date) ? date : null,
        Phase = Phase,
        Channel = Channel,
        Asset = Asset,
        Audience = Audience,
        Owner = Owner,
        Link = Link,
        Status = Status,
        Result = Result,
        RouteAffinity = RouteAffinity,
    };
}

public partial class RightsContractEditorViewModel : ObservableObject
{
    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    [ObservableProperty]
    private string _party = string.Empty;

    [ObservableProperty]
    private string _rightOrService = string.Empty;

    [ObservableProperty]
    private string _territory = string.Empty;

    [ObservableProperty]
    private string _format = string.Empty;

    [ObservableProperty]
    private string _startDateText = string.Empty;

    [ObservableProperty]
    private string _endDateText = string.Empty;

    [ObservableProperty]
    private string _paymentText = string.Empty;

    [ObservableProperty]
    private string _paymentNotes = string.Empty;

    [ObservableProperty]
    private string _restrictions = string.Empty;

    [ObservableProperty]
    private string _agreementFile = string.Empty;

    [ObservableProperty]
    private RightsContractStatus _status;

    public static RightsContractEditorViewModel From(RightsAndContract source) => new()
    {
        Id = source.Id,
        ProjectId = source.ProjectId,
        Party = source.Party,
        RightOrService = source.RightOrService,
        Territory = source.Territory,
        Format = source.Format,
        StartDateText = source.StartDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        EndDateText = source.EndOrReversionDate?.ToString("yyyy-MM-dd") ?? string.Empty,
        PaymentText = source.Payment?.ToString("0.##") ?? string.Empty,
        PaymentNotes = source.PaymentNotes,
        Restrictions = source.Restrictions,
        AgreementFile = source.AgreementFile,
        Status = source.Status,
    };

    public RightsAndContract ToModel()
    {
        decimal? payment = null;
        if (!string.IsNullOrWhiteSpace(PaymentText))
        {
            if (!decimal.TryParse(PaymentText, out var parsed))
            {
                throw new InvalidOperationException("Payment must be a valid number.");
            }

            payment = parsed;
        }

        return new RightsAndContract
        {
            Id = Id,
            ProjectId = ProjectId,
            Party = Party,
            RightOrService = RightOrService,
            Territory = Territory,
            Format = Format,
            StartDate = DateOnly.TryParse(StartDateText, out var start) ? start : null,
            EndOrReversionDate = DateOnly.TryParse(EndDateText, out var end) ? end : null,
            Payment = payment,
            PaymentNotes = PaymentNotes,
            Restrictions = Restrictions,
            AgreementFile = AgreementFile,
            Status = Status,
        };
    }
}
