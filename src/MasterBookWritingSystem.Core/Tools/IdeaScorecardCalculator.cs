using MasterBookWritingSystem.Core.Domain.Tools;

namespace MasterBookWritingSystem.Core.Tools;

public static class IdeaScorecardCalculator
{
    public static readonly IReadOnlyList<string> Criteria =
    [
        "Fascination",
        "Emotional Power",
        "Conflict",
        "Character",
        "Visual",
        "Original Combination",
        "Novel Length",
        "Difficult Choices",
        "Audience Fit",
        "Series Fit",
    ];

    public const int MaxPerCriterion = 10;

    public const int MaxTotal = 100;

    public static int ClampScore(int value) => Math.Clamp(value, 0, MaxPerCriterion);

    public static int CalculateTotal(IdeaScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        return ClampScore(score.Fascination)
            + ClampScore(score.EmotionalPower)
            + ClampScore(score.Conflict)
            + ClampScore(score.Character)
            + ClampScore(score.Visual)
            + ClampScore(score.OriginalCombination)
            + ClampScore(score.NovelLength)
            + ClampScore(score.DifficultChoices)
            + ClampScore(score.AudienceFit)
            + ClampScore(score.SeriesFit);
    }

    /// <summary>
    /// Decision bands: Pursue ≥70, Develop 50–69, Park 30–49, Discard &lt;30.
    /// </summary>
    public static string Decide(int total) => total switch
    {
        >= 70 => "Pursue",
        >= 50 => "Develop",
        >= 30 => "Park",
        _ => "Discard",
    };

    public static IdeaScore Apply(IdeaScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        score.Fascination = ClampScore(score.Fascination);
        score.EmotionalPower = ClampScore(score.EmotionalPower);
        score.Conflict = ClampScore(score.Conflict);
        score.Character = ClampScore(score.Character);
        score.Visual = ClampScore(score.Visual);
        score.OriginalCombination = ClampScore(score.OriginalCombination);
        score.NovelLength = ClampScore(score.NovelLength);
        score.DifficultChoices = ClampScore(score.DifficultChoices);
        score.AudienceFit = ClampScore(score.AudienceFit);
        score.SeriesFit = ClampScore(score.SeriesFit);
        score.Total = CalculateTotal(score);
        score.Decision = Decide(score.Total);
        score.ScoredUtc = DateTimeOffset.UtcNow;
        return score;
    }

    public static string ExplainDecision(int total)
        => $"Total {total}/{MaxTotal}. Bands: Pursue ≥70, Develop 50–69, Park 30–49, Discard <30. Each of {Criteria.Count} criteria is scored 0–{MaxPerCriterion}.";
}
