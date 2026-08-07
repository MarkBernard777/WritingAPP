namespace MasterBookWritingSystem.Core.Tools;

public sealed class DraftPlannerInput
{
    public int TargetWordCount { get; set; }

    public int WordsCompleted { get; set; }

    public int WordsPerSession { get; set; } = 1000;

    public int SessionsPerWeek { get; set; } = 5;

    public DateOnly? StartDate { get; set; }
}

public sealed class DraftPlannerResult
{
    public required int WordsRemaining { get; init; }

    public required int SessionsRequired { get; init; }

    public required double WeeksRequired { get; init; }

    public required int WordsPerWeek { get; init; }

    public required int RequiredDailyPace { get; init; }

    public DateOnly? EstimatedCompletionDate { get; init; }

    public required bool IsValid { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public string Explanation { get; init; } =
        "Remaining words ÷ words per session = sessions required. Sessions ÷ sessions/week = weeks. Daily pace assumes 7-day weeks.";
}

public static class DraftPlannerCalculator
{
    public static DraftPlannerResult Calculate(DraftPlannerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new List<string>();
        if (input.TargetWordCount <= 0)
        {
            errors.Add("Target word count must be greater than zero.");
        }

        if (input.WordsCompleted < 0)
        {
            errors.Add("Words completed cannot be negative.");
        }

        if (input.WordsPerSession <= 0)
        {
            errors.Add("Words per session must be greater than zero.");
        }

        if (input.SessionsPerWeek <= 0)
        {
            errors.Add("Sessions per week must be greater than zero.");
        }

        if (errors.Count > 0)
        {
            return new DraftPlannerResult
            {
                WordsRemaining = 0,
                SessionsRequired = 0,
                WeeksRequired = 0,
                WordsPerWeek = 0,
                RequiredDailyPace = 0,
                IsValid = false,
                Errors = errors,
            };
        }

        var remaining = Math.Max(0, input.TargetWordCount - input.WordsCompleted);
        var sessions = remaining == 0
            ? 0
            : (int)Math.Ceiling(remaining / (double)input.WordsPerSession);
        var wordsPerWeek = input.WordsPerSession * input.SessionsPerWeek;
        var weeks = remaining == 0 || wordsPerWeek == 0
            ? 0
            : remaining / (double)wordsPerWeek;
        var dailyPace = remaining == 0 ? 0 : (int)Math.Ceiling(remaining / Math.Max(weeks * 7d, 1d));
        DateOnly? completion = null;
        if (input.StartDate is { } start && weeks > 0)
        {
            var days = (int)Math.Ceiling(weeks * 7d);
            completion = start.AddDays(days);
        }
        else if (remaining == 0 && input.StartDate is { } done)
        {
            completion = done;
        }

        return new DraftPlannerResult
        {
            WordsRemaining = remaining,
            SessionsRequired = sessions,
            WeeksRequired = Math.Round(weeks, 2),
            WordsPerWeek = wordsPerWeek,
            RequiredDailyPace = dailyPace,
            EstimatedCompletionDate = completion,
            IsValid = true,
            Errors = [],
        };
    }
}
