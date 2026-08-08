using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Services;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Drafting;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class ProgressViewModel : ObservableObject
{
    private readonly IProjectService _projects;
    private readonly IDraftingTargetService _targets;
    private readonly IDraftingTimerService _timer;
    private readonly IDraftingProgressQueryService _progress;
    private readonly IProjectDialogService _dialogs;
    private readonly TimeProvider _clock;

    public ProgressViewModel(
        IProjectService projects,
        IDraftingTargetService targets,
        IDraftingTimerService timer,
        IDraftingProgressQueryService progress,
        IProjectDialogService dialogs,
        TimeProvider clock)
    {
        _projects = projects;
        _targets = targets;
        _timer = timer;
        _progress = progress;
        _dialogs = dialogs;
        _clock = clock;
        _timer.Changed += (_, _) => SyncTimer();
        SyncTimer();
        _ = RefreshAsync();
    }

    public ObservableCollection<DraftingProgressBucket> ByDate { get; } = [];

    public ObservableCollection<DraftingProgressBucket> ByChapter { get; } = [];

    public ObservableCollection<DraftingProgressBucket> ByScene { get; } = [];

    public ObservableCollection<DraftingProgressBucket> ByViewpoint { get; } = [];

    [ObservableProperty] private bool _hasProject;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int _dailyWordGoal;
    [ObservableProperty] private int _weeklyWordGoal;
    [ObservableProperty] private bool _targetsEnabled;
    [ObservableProperty] private string _timerStateLabel = "Stopped";
    [ObservableProperty] private string _timerElapsedDisplay = "00:00:00";
    [ObservableProperty] private bool _canPause;
    [ObservableProperty] private bool _canResume;
    [ObservableProperty] private bool _canStop;
    [ObservableProperty] private bool _hasInterruptedSession;
    [ObservableProperty] private string _interruptedSummary = string.Empty;
    [ObservableProperty] private DateTime _rangeFrom = DateTime.Today;
    [ObservableProperty] private DateTime _rangeTo = DateTime.Today;
    [ObservableProperty] private string _progressSummary = string.Empty;
    [ObservableProperty] private string _todaySummary = string.Empty;
    [ObservableProperty] private string _weekSummary = string.Empty;
    [ObservableProperty] private string _idleThresholdHelp =
        $"Auto-sessions close after {DraftingSessionRules.IdleThreshold.TotalMinutes:0} minutes without a meaningful word-count change.";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var project = _projects.ActiveProject;
        HasProject = project is not null;
        if (project is null)
        {
            StatusMessage = "Open a project to track drafting progress.";
            return;
        }

        try
        {
            var target = await _targets.GetTargetAsync(project.Id).ConfigureAwait(true);
            DailyWordGoal = target.DailyWordGoal;
            WeeklyWordGoal = target.WeeklyWordGoal;
            TargetsEnabled = target.IsEnabled;

            var now = _clock.GetUtcNow();
            var (todayFrom, todayTo) = DraftingProgressAggregator.TodayRange(now, _clock.LocalTimeZone);
            var (weekFrom, weekTo) = DraftingProgressAggregator.ThisWeekRange(now, _clock.LocalTimeZone);
            var today = await _progress.GetProgressAsync(project.Id, todayFrom, todayTo).ConfigureAwait(true);
            var week = await _progress.GetProgressAsync(project.Id, weekFrom, weekTo).ConfigureAwait(true);
            TodaySummary = FormatReport("Today", today, target.DailyWordGoal, target.IsEnabled);
            WeekSummary = FormatReport("This week", week, target.WeeklyWordGoal, target.IsEnabled);

            await LoadRangeAsync().ConfigureAwait(true);
            SyncTimer();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveTargetsAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        try
        {
            await _targets.SaveTargetAsync(new DraftingTarget
            {
                ProjectId = project.Id,
                DailyWordGoal = DailyWordGoal,
                WeeklyWordGoal = WeeklyWordGoal,
                IsEnabled = TargetsEnabled,
            }).ConfigureAwait(true);
            StatusMessage = "Drafting targets saved.";
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            _dialogs.ShowMessage(ex.Message, "Drafting targets");
        }
    }

    [RelayCommand]
    private async Task StartTimerAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        await _timer.StartAsync(project.Id).ConfigureAwait(true);
        SyncTimer();
    }

    [RelayCommand]
    private async Task PauseTimerAsync()
    {
        await _timer.PauseAsync().ConfigureAwait(true);
        SyncTimer();
    }

    [RelayCommand]
    private async Task ResumeTimerAsync()
    {
        await _timer.ResumeAsync().ConfigureAwait(true);
        SyncTimer();
    }

    [RelayCommand]
    private async Task StopTimerAsync()
    {
        await _timer.StopAsync().ConfigureAwait(true);
        SyncTimer();
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResumeInterruptedAsync()
    {
        var snapshot = _timer.GetSnapshot();
        if (snapshot.InterruptedSession is null)
        {
            return;
        }

        await _timer.ResumeInterruptedAsync(snapshot.InterruptedSession.Id).ConfigureAwait(true);
        SyncTimer();
    }

    [RelayCommand]
    private async Task CloseInterruptedAsync()
    {
        var snapshot = _timer.GetSnapshot();
        if (snapshot.InterruptedSession is null)
        {
            return;
        }

        if (!_dialogs.Confirm(
                "Close the interrupted session without inventing active time beyond the last recorded activity?",
                "Close interrupted session"))
        {
            return;
        }

        await _timer.CloseInterruptedAsync(snapshot.InterruptedSession.Id).ConfigureAwait(true);
        SyncTimer();
        await RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadRangeAsync()
    {
        var project = _projects.ActiveProject;
        if (project is null)
        {
            return;
        }

        var from = DateOnly.FromDateTime(RangeFrom.Date);
        var to = DateOnly.FromDateTime(RangeTo.Date);
        var report = await _progress.GetProgressAsync(project.Id, from, to).ConfigureAwait(true);
        ByDate.Clear();
        ByChapter.Clear();
        ByScene.Clear();
        ByViewpoint.Clear();
        foreach (var item in report.ByDate)
        {
            ByDate.Add(item);
        }

        foreach (var item in report.ByChapter)
        {
            ByChapter.Add(item);
        }

        foreach (var item in report.ByScene)
        {
            ByScene.Add(item);
        }

        foreach (var item in report.ByViewpoint)
        {
            ByViewpoint.Add(item);
        }

        ProgressSummary =
            $"{report.SessionCount} session(s) · {report.NetWords:+0;-0;0} words · {FormatDuration(report.ActiveDuration)} active ({report.TimeZoneId})";
    }

    [RelayCommand]
    private async Task ShowTodayAsync()
    {
        var (from, to) = DraftingProgressAggregator.TodayRange(_clock.GetUtcNow(), _clock.LocalTimeZone);
        RangeFrom = from.ToDateTime(TimeOnly.MinValue);
        RangeTo = to.ToDateTime(TimeOnly.MinValue);
        await LoadRangeAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ShowThisWeekAsync()
    {
        var (from, to) = DraftingProgressAggregator.ThisWeekRange(_clock.GetUtcNow(), _clock.LocalTimeZone);
        RangeFrom = from.ToDateTime(TimeOnly.MinValue);
        RangeTo = to.ToDateTime(TimeOnly.MinValue);
        await LoadRangeAsync().ConfigureAwait(true);
    }

    private void SyncTimer()
    {
        var snapshot = _timer.GetSnapshot();
        TimerStateLabel = snapshot.State.ToString();
        TimerElapsedDisplay = FormatDuration(snapshot.ActiveElapsed);
        CanPause = snapshot.State == DraftingTimerState.Running;
        CanResume = snapshot.State == DraftingTimerState.Paused;
        CanStop = snapshot.State is DraftingTimerState.Running or DraftingTimerState.Paused;
        HasInterruptedSession = snapshot.HasInterruptedSession;
        InterruptedSummary = snapshot.InterruptedSession is null
            ? string.Empty
            : $"Interrupted session from {snapshot.InterruptedSession.StartedUtc:u} · recorded active {FormatDuration(snapshot.InterruptedSession.ActiveDuration)} · net {snapshot.InterruptedSession.NetWordChange:+0;-0;0} words";
    }

    private static string FormatReport(string label, DraftingProgressReport report, int goal, bool enabled)
    {
        var goalText = enabled && goal > 0
            ? $" · goal {goal} ({Math.Clamp(100.0 * report.NetWords / goal, 0, 999):0}%)"
            : string.Empty;
        return $"{label}: {report.NetWords:+0;-0;0} words · {FormatDuration(report.ActiveDuration)}{goalText}";
    }

    private static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }
}
