using MasterBookWritingSystem.Core.Domain.Tools;

namespace MasterBookWritingSystem.Core.Tools;

public sealed class BetaThemeGroup
{
    public required string Key { get; init; }

    public required int Count { get; init; }

    public required IReadOnlyList<string> Observations { get; init; }

    public required IReadOnlyList<string> SuggestedFixes { get; init; }
}

public sealed class BetaFeedbackSynthesisResult
{
    public required int EntryCount { get; init; }

    public required IReadOnlyList<BetaThemeGroup> GroupsByCategory { get; init; }

    public required IReadOnlyList<BetaThemeGroup> GroupsByObservation { get; init; }

    public required int ObservationOnlyCount { get; init; }

    public required int SuggestionCount { get; init; }

    public string Explanation { get; init; } =
        "Local grouping only: identical category/observation keys are counted. Observations and suggested fixes are listed separately. This is not AI interpretation.";
}

public static class BetaFeedbackSynthesizer
{
    public static BetaFeedbackSynthesisResult Synthesize(IEnumerable<BetaFeedbackEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToList();
        var byCategory = list
            .GroupBy(entry => Normalize(entry.Category, "Uncategorized"))
            .Select(group => new BetaThemeGroup
            {
                Key = group.Key,
                Count = group.Count(),
                Observations = group.Select(item => item.Observation.Trim())
                    .Where(item => item.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SuggestedFixes = group.Select(item => item.SuggestedFix.Trim())
                    .Where(item => item.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byObservation = list
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Observation))
            .GroupBy(entry => Normalize(entry.Observation, "Observation"))
            .Select(group => new BetaThemeGroup
            {
                Key = group.Key,
                Count = group.Count(),
                Observations = [group.Key],
                SuggestedFixes = group.Select(item => item.SuggestedFix.Trim())
                    .Where(item => item.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BetaFeedbackSynthesisResult
        {
            EntryCount = list.Count,
            GroupsByCategory = byCategory,
            GroupsByObservation = byObservation,
            ObservationOnlyCount = list.Count(item =>
                !string.IsNullOrWhiteSpace(item.Observation) && string.IsNullOrWhiteSpace(item.SuggestedFix)),
            SuggestionCount = list.Count(item => !string.IsNullOrWhiteSpace(item.SuggestedFix)),
        };
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
