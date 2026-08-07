namespace MasterBookWritingSystem.Core.Tools;

public sealed class PremiseComponents
{
    public string Protagonist { get; set; } = string.Empty;

    public string Disruption { get; set; } = string.Empty;

    public string Goal { get; set; } = string.Empty;

    public string Opposition { get; set; } = string.Empty;

    public string Stakes { get; set; } = string.Empty;

    public string Urgency { get; set; } = string.Empty;

    public string Transformation { get; set; } = string.Empty;

    public string FinalSentence { get; set; } = string.Empty;
}

public sealed class PremiseBuildResult
{
    public required string GeneratedSentence { get; init; }

    public required IReadOnlyList<string> MissingComponents { get; init; }

    public required bool IsComplete { get; init; }

    public string Explanation { get; init; } =
        "A working premise joins protagonist, disruption, goal, opposition, stakes, urgency and transformation.";
}

public static class PremiseBuilderCalculator
{
    public static readonly IReadOnlyList<(string Key, string Label)> ComponentKeys =
    [
        ("PROTAGONIST", "Protagonist"),
        ("DISRUPTION", "Disruption"),
        ("GOAL", "Goal"),
        ("OPPOSITION", "Opposition"),
        ("STAKES", "Stakes"),
        ("URGENCY", "Urgency"),
        ("TRANSFORMATION", "Transformation"),
    ];

    public static PremiseBuildResult Build(PremiseComponents components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var missing = new List<string>();
        void Check(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                missing.Add(label);
            }
        }

        Check("Protagonist", components.Protagonist);
        Check("Disruption", components.Disruption);
        Check("Goal", components.Goal);
        Check("Opposition", components.Opposition);
        Check("Stakes", components.Stakes);
        Check("Urgency", components.Urgency);
        Check("Transformation", components.Transformation);

        if (!string.IsNullOrWhiteSpace(components.FinalSentence))
        {
            return new PremiseBuildResult
            {
                GeneratedSentence = components.FinalSentence.Trim(),
                MissingComponents = missing,
                IsComplete = missing.Count == 0,
            };
        }

        var sentence = missing.Count == 0
            ? $"When {Trim(components.Disruption)}, {Trim(components.Protagonist)} must {Trim(components.Goal)} despite {Trim(components.Opposition)}, or else {Trim(components.Stakes)}, because {Trim(components.Urgency)}, and will be changed by {Trim(components.Transformation)}."
            : "Fill every premise component to generate a working sentence, or supply a final sentence manually.";

        return new PremiseBuildResult
        {
            GeneratedSentence = sentence,
            MissingComponents = missing,
            IsComplete = missing.Count == 0,
        };
    }

    private static string Trim(string value) => value.Trim().TrimEnd('.');
}
