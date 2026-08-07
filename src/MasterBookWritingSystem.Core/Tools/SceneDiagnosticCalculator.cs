using MasterBookWritingSystem.Core.Domain.Story;

namespace MasterBookWritingSystem.Core.Tools;

public sealed class SceneDiagnosticResult
{
    public required Guid SceneId { get; init; }

    public required string Title { get; init; }

    public required int Score { get; init; }

    public required int MaxScore { get; init; }

    public required IReadOnlyList<string> PresentParts { get; init; }

    public required IReadOnlyList<string> MissingParts { get; init; }

    public required string PurposeAssessment { get; init; }

    public string Explanation { get; init; } =
        "Seven-part model: Situation, Goal, Obstacle, Escalation, Choice, Turn, Consequence. Purpose fails if Goal and Consequence are both empty.";
}

public static class SceneDiagnosticCalculator
{
    public static readonly IReadOnlyList<string> DramaticParts =
    [
        "Situation",
        "Goal",
        "Obstacle",
        "Escalation",
        "Choice",
        "Turn",
        "Consequence",
    ];

    public static SceneDiagnosticResult Evaluate(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var present = new List<string>();
        var missing = new List<string>();

        void Part(string name, params string[] values)
        {
            if (values.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                present.Add(name);
            }
            else
            {
                missing.Add(name);
            }
        }

        Part("Situation", scene.Location, scene.Time, scene.MainEvent);
        Part("Goal", scene.Goal);
        Part("Obstacle", scene.Opposition);
        Part("Escalation", scene.Stakes, scene.MainEvent);
        Part("Choice", scene.Choice);
        Part("Turn", scene.EmotionalTurn, scene.Revelation);
        Part("Consequence", scene.Consequence, scene.Outcome);

        var purposeOk = present.Contains("Goal") && present.Contains("Consequence");
        var purpose = purposeOk
            ? "Purpose clear: the viewpoint character pursues a goal that produces a consequence."
            : "Purpose weak: add a clear Goal and Consequence so the scene changes the story.";

        return new SceneDiagnosticResult
        {
            SceneId = scene.Id,
            Title = scene.Title,
            Score = present.Count,
            MaxScore = DramaticParts.Count,
            PresentParts = present,
            MissingParts = missing,
            PurposeAssessment = purpose,
        };
    }
}
