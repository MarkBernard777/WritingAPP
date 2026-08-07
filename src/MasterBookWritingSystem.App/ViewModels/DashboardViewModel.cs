using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Dashboard;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardService _dashboardService;

    public DashboardViewModel(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
        _ = RefreshAsync();
    }

    [ObservableProperty]
    private bool _hasProject;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _genre = string.Empty;

    [ObservableProperty]
    private string _publishingRoute = string.Empty;

    [ObservableProperty]
    private string _northStar = string.Empty;

    [ObservableProperty]
    private string _rootPath = string.Empty;

    [ObservableProperty]
    private int _applicablePhaseCount;

    [ObservableProperty]
    private int _completedStepCount;

    [ObservableProperty]
    private int _applicableStepCount;

    [ObservableProperty]
    private int _passedGateCount;

    [ObservableProperty]
    private string _currentPhase = "—";

    [ObservableProperty]
    private string _currentDeliverable = "—";

    [ObservableProperty]
    private string _nextAction = "Open or create a project to begin.";

    [ObservableProperty]
    private int _wordCount;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var summary = await _dashboardService.BuildAsync().ConfigureAwait(true);
            Apply(summary);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void Apply(ProjectDashboardSummary? summary)
    {
        HasProject = summary is not null;
        if (summary is null)
        {
            Title = string.Empty;
            Author = string.Empty;
            Genre = string.Empty;
            PublishingRoute = string.Empty;
            NorthStar = string.Empty;
            RootPath = string.Empty;
            ApplicablePhaseCount = 0;
            CompletedStepCount = 0;
            ApplicableStepCount = 0;
            PassedGateCount = 0;
            CurrentPhase = "—";
            CurrentDeliverable = "—";
            NextAction = "Open or create a project to begin.";
            WordCount = 0;
            return;
        }

        Title = summary.Title;
        Author = summary.Author;
        Genre = summary.Genre;
        PublishingRoute = summary.PublishingRoute.ToString();
        NorthStar = summary.NorthStar;
        RootPath = summary.RootPath;
        ApplicablePhaseCount = summary.ApplicablePhaseCount;
        CompletedStepCount = summary.CompletedStepCount;
        ApplicableStepCount = summary.ApplicableStepCount;
        PassedGateCount = summary.PassedGateCount;
        CurrentPhase = summary.CurrentPhaseId is null
            ? "All applicable phases complete"
            : $"{summary.CurrentPhaseId}. {summary.CurrentPhaseTitle}";
        CurrentDeliverable = string.IsNullOrWhiteSpace(summary.CurrentDeliverable)
            ? "—"
            : summary.CurrentDeliverable;
        NextAction = summary.NextAction;
        WordCount = summary.WordCount;
        StatusMessage = string.Empty;
    }
}
