namespace MasterBookWritingSystem.Core.Drafting;

/// <summary>
/// Rules for automatic drafting session logging.
/// Idle threshold: after <see cref="IdleThreshold"/> of wall-clock time without a
/// meaningful prose word-count change, an open auto-session is closed with
/// <see cref="DraftingSessionCompletionReason.IdleTimeout"/>. Sessions are not
/// opened for metadata-only edits or a single accidental keystroke that does not
/// change word count by at least <see cref="MeaningfulWordDeltaThreshold"/>.
/// </summary>
public static class DraftingSessionRules
{
    public static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);

    public const int MeaningfulWordDeltaThreshold = 1;

    public static bool IsMeaningfulWordDelta(int previousWordCount, int nextWordCount)
        => Math.Abs(nextWordCount - previousWordCount) >= MeaningfulWordDeltaThreshold;
}

public enum DraftingSessionCompletionReason
{
    None = 0,
    ManualStop = 1,
    IdleTimeout = 2,
    Interrupted = 3,
    ClosedAfterInterrupt = 4,
}

public enum DraftingTimerState
{
    Stopped = 0,
    Running = 1,
    Paused = 2,
}

public sealed class DraftingTarget
{
    public required Guid ProjectId { get; init; }

    public int DailyWordGoal { get; set; }

    public int WeeklyWordGoal { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

public static class DraftingTargetValidation
{
    public static void EnsureValid(DraftingTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ProjectId == Guid.Empty)
        {
            throw new InvalidOperationException("Drafting target requires a project id.");
        }

        if (target.DailyWordGoal < 0)
        {
            throw new InvalidOperationException("Daily word goal cannot be negative.");
        }

        if (target.WeeklyWordGoal < 0)
        {
            throw new InvalidOperationException("Weekly word goal cannot be negative.");
        }

        if (target.IsEnabled && target.DailyWordGoal == 0 && target.WeeklyWordGoal == 0)
        {
            throw new InvalidOperationException(
                "Enable a drafting target only when a daily or weekly word goal is set.");
        }

        if (target.WeeklyWordGoal > 0
            && target.DailyWordGoal > 0
            && target.WeeklyWordGoal < target.DailyWordGoal)
        {
            throw new InvalidOperationException(
                "Weekly word goal cannot be less than the daily word goal.");
        }
    }
}

public sealed class DraftingSession
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public Guid? BookId { get; init; }

    public Guid? PartId { get; init; }

    public Guid? ChapterId { get; init; }

    public Guid? SceneId { get; init; }

    public Guid? ViewpointCharacterId { get; init; }

    public string? ViewpointLabel { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset? EndedUtc { get; init; }

    public DateTimeOffset LastActivityUtc { get; init; }

    public TimeSpan ActiveDuration { get; init; }

    public int StartingWordCount { get; init; }

    public int EndingWordCount { get; init; }

    public int NetWordChange { get; init; }

    public DraftingSessionCompletionReason CompletionReason { get; init; }

    public bool IsOpen => EndedUtc is null
        && CompletionReason is DraftingSessionCompletionReason.None
            or DraftingSessionCompletionReason.Interrupted;

    public bool IsInterrupted => CompletionReason == DraftingSessionCompletionReason.Interrupted
        && EndedUtc is null;
}

public sealed class DraftingProgressBucket
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public int NetWords { get; init; }

    public TimeSpan ActiveDuration { get; init; }

    public int SessionCount { get; init; }
}

public sealed class DraftingProgressReport
{
    public required DateOnly FromLocalDate { get; init; }

    public required DateOnly ToLocalDate { get; init; }

    public required string TimeZoneId { get; init; }

    public int NetWords { get; init; }

    public TimeSpan ActiveDuration { get; init; }

    public int SessionCount { get; init; }

    public IReadOnlyList<DraftingProgressBucket> ByDate { get; init; } = [];

    public IReadOnlyList<DraftingProgressBucket> ByChapter { get; init; } = [];

    public IReadOnlyList<DraftingProgressBucket> ByScene { get; init; } = [];

    public IReadOnlyList<DraftingProgressBucket> ByViewpoint { get; init; } = [];
}

public static class DraftingProgressAggregator
{
    public static DraftingProgressReport Aggregate(
        IEnumerable<DraftingSession> sessions,
        DateOnly fromLocalDate,
        DateOnly toLocalDate,
        TimeZoneInfo timeZone,
        IReadOnlyDictionary<Guid, string>? chapterTitles = null,
        IReadOnlyDictionary<Guid, string>? sceneTitles = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (toLocalDate < fromLocalDate)
        {
            throw new InvalidOperationException("Progress range end cannot precede the start.");
        }

        chapterTitles ??= new Dictionary<Guid, string>();
        sceneTitles ??= new Dictionary<Guid, string>();

        var inRange = new List<(DraftingSession Session, DateOnly LocalDate)>();
        foreach (var session in sessions)
        {
            if (session.CompletionReason == DraftingSessionCompletionReason.Interrupted
                && session.EndedUtc is null)
            {
                continue;
            }

            var local = TimeZoneInfo.ConvertTime(session.StartedUtc, timeZone);
            var date = DateOnly.FromDateTime(local.DateTime);
            if (date < fromLocalDate || date > toLocalDate)
            {
                continue;
            }

            inRange.Add((session, date));
        }

        var byDate = inRange
            .GroupBy(item => item.LocalDate)
            .OrderBy(group => group.Key)
            .Select(group => ToBucket(
                group.Key.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                group.Key.ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture),
                group.Select(item => item.Session)))
            .ToList();

        var byChapter = inRange
            .Where(item => item.Session.ChapterId is not null)
            .GroupBy(item => item.Session.ChapterId!.Value)
            .OrderByDescending(group => group.Sum(item => item.Session.NetWordChange))
            .Select(group => ToBucket(
                group.Key.ToString("N"),
                chapterTitles.GetValueOrDefault(group.Key, "Chapter"),
                group.Select(item => item.Session)))
            .ToList();

        var byScene = inRange
            .Where(item => item.Session.SceneId is not null)
            .GroupBy(item => item.Session.SceneId!.Value)
            .OrderByDescending(group => group.Sum(item => item.Session.NetWordChange))
            .Select(group => ToBucket(
                group.Key.ToString("N"),
                sceneTitles.GetValueOrDefault(group.Key, "Scene"),
                group.Select(item => item.Session)))
            .ToList();

        var byViewpoint = inRange
            .Where(item => !string.IsNullOrWhiteSpace(item.Session.ViewpointLabel)
                || item.Session.ViewpointCharacterId is not null)
            .GroupBy(item => item.Session.ViewpointLabel
                ?? item.Session.ViewpointCharacterId?.ToString("N")
                ?? "Unknown")
            .OrderByDescending(group => group.Sum(item => item.Session.NetWordChange))
            .Select(group => ToBucket(group.Key, group.Key, group.Select(item => item.Session)))
            .ToList();

        return new DraftingProgressReport
        {
            FromLocalDate = fromLocalDate,
            ToLocalDate = toLocalDate,
            TimeZoneId = timeZone.Id,
            NetWords = inRange.Sum(item => item.Session.NetWordChange),
            ActiveDuration = TimeSpan.FromSeconds(
                inRange.Sum(item => item.Session.ActiveDuration.TotalSeconds)),
            SessionCount = inRange.Count,
            ByDate = byDate,
            ByChapter = byChapter,
            ByScene = byScene,
            ByViewpoint = byViewpoint,
        };
    }

    public static (DateOnly From, DateOnly To) TodayRange(DateTimeOffset utcNow, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var today = DateOnly.FromDateTime(local.DateTime);
        return (today, today);
    }

    public static (DateOnly From, DateOnly To) ThisWeekRange(DateTimeOffset utcNow, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        var today = DateOnly.FromDateTime(local.DateTime);
        var start = today.AddDays(-(int)today.DayOfWeek);
        return (start, today);
    }

    private static DraftingProgressBucket ToBucket(
        string key,
        string label,
        IEnumerable<DraftingSession> sessions)
    {
        var list = sessions.ToList();
        return new DraftingProgressBucket
        {
            Key = key,
            Label = label,
            NetWords = list.Sum(item => item.NetWordChange),
            ActiveDuration = TimeSpan.FromSeconds(list.Sum(item => item.ActiveDuration.TotalSeconds)),
            SessionCount = list.Count,
        };
    }
}
