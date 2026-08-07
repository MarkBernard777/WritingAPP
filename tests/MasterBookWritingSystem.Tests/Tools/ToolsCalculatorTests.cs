using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Domain.Tools;
using MasterBookWritingSystem.Core.Tools;

namespace MasterBookWritingSystem.Tests.Tools;

public class ToolsCalculatorTests
{
    [Fact]
    public void IdeaScorecard_Clamps_Sums_AndDecides()
    {
        var score = IdeaScorecardCalculator.Apply(new IdeaScore
        {
            Id = Guid.NewGuid(),
            IdeaId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Fascination = 11,
            EmotionalPower = -2,
            Conflict = 10,
            Character = 10,
            Visual = 10,
            OriginalCombination = 10,
            NovelLength = 10,
            DifficultChoices = 10,
            AudienceFit = 10,
            SeriesFit = 10,
        });

        Assert.Equal(10, score.Fascination);
        Assert.Equal(0, score.EmotionalPower);
        Assert.Equal(90, score.Total);
        Assert.Equal("Pursue", score.Decision);

        Assert.Equal("Discard", IdeaScorecardCalculator.Decide(0));
        Assert.Equal("Park", IdeaScorecardCalculator.Decide(30));
        Assert.Equal("Develop", IdeaScorecardCalculator.Decide(50));
        Assert.Equal("Pursue", IdeaScorecardCalculator.Decide(70));
    }

    [Fact]
    public void PremiseBuilder_RequiresSevenComponents()
    {
        var incomplete = PremiseBuilderCalculator.Build(new PremiseComponents());
        Assert.False(incomplete.IsComplete);
        Assert.Equal(7, incomplete.MissingComponents.Count);

        var complete = PremiseBuilderCalculator.Build(new PremiseComponents
        {
            Protagonist = "a guard",
            Disruption = "an oath breaks",
            Goal = "escort the prisoner",
            Opposition = "civil war",
            Stakes = "the realm falls",
            Urgency = "winter closes the passes",
            Transformation = "learning mercy",
        });
        Assert.True(complete.IsComplete);
        Assert.Contains("guard", complete.GeneratedSentence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DraftPlanner_HandlesZeroRemaining_AndInvalidInputs()
    {
        var invalid = DraftPlannerCalculator.Calculate(new DraftPlannerInput());
        Assert.False(invalid.IsValid);
        Assert.NotEmpty(invalid.Errors);

        var done = DraftPlannerCalculator.Calculate(new DraftPlannerInput
        {
            TargetWordCount = 1000,
            WordsCompleted = 1000,
            WordsPerSession = 500,
            SessionsPerWeek = 2,
            StartDate = new DateOnly(2026, 1, 1),
        });
        Assert.True(done.IsValid);
        Assert.Equal(0, done.WordsRemaining);
        Assert.Equal(0, done.SessionsRequired);

        var plan = DraftPlannerCalculator.Calculate(new DraftPlannerInput
        {
            TargetWordCount = 10000,
            WordsCompleted = 0,
            WordsPerSession = 1000,
            SessionsPerWeek = 5,
            StartDate = new DateOnly(2026, 1, 1),
        });
        Assert.Equal(10, plan.SessionsRequired);
        Assert.Equal(2, plan.WeeksRequired);
        Assert.NotNull(plan.EstimatedCompletionDate);
    }

    [Fact]
    public void SceneDiagnostic_ScoresSevenParts()
    {
        var empty = SceneDiagnosticCalculator.Evaluate(new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            SequenceNumber = 1,
            Title = "Empty",
        });
        Assert.Equal(0, empty.Score);
        Assert.Equal(7, empty.MissingParts.Count);

        var full = SceneDiagnosticCalculator.Evaluate(new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            SequenceNumber = 1,
            Title = "Clash",
            Location = "Harbor",
            Goal = "Escape",
            Opposition = "Guards",
            Stakes = "Arrest",
            Choice = "Fight",
            EmotionalTurn = "Fear to resolve",
            Consequence = "Ally lost",
        });
        Assert.Equal(7, full.Score);
        Assert.Empty(full.MissingParts);
    }

    [Fact]
    public void TextAnalyzer_HandlesEmpty_AndFindsSignals()
    {
        var empty = TextAnalyzerCalculator.Analyze("");
        Assert.Equal(0, empty.WordCount);
        Assert.Equal(0, empty.ReadingTimeMinutes);

        var longSentence = string.Join(' ', Enumerable.Range(1, 30).Select(i => $"word{i}")) + ".";
        var text = string.Join(' ', Enumerable.Repeat("shadow", 6))
            + ". " + longSentence + " She really really noticed the door. Need a [FIX] marker.";
        var result = TextAnalyzerCalculator.Analyze(text);
        Assert.True(result.WordCount > 20);
        Assert.True(result.LongSentenceCount >= 1);
        Assert.Contains(result.RepeatedTerms, item => item.Term.Equals("shadow", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.FilterWords, item => item.Word == "really");
        Assert.NotEmpty(result.DraftingMarkers);
    }

    [Fact]
    public void PublishingRoute_IsDeterministic_WithTransparentTotals()
    {
        var preferControl = PublishingRouteDecisionCalculator.Decide(new PublishingRoutePreferences
        {
            ControlWeight = 10,
            FundingWeight = 1,
            SpeedWeight = 1,
            DistributionWeight = 1,
            RightsWeight = 10,
            ProductionWeight = 1,
        });
        Assert.Equal(PublishingRoute.SelfPublishing, preferControl.RecommendedRoute);
        Assert.Equal(3, preferControl.Breakdown.Count);
        Assert.True(preferControl.Breakdown.Single(item => item.Route == PublishingRoute.SelfPublishing).Total
            >= preferControl.Breakdown.Max(item => item.Total));

        var preferPublisher = PublishingRouteDecisionCalculator.Decide(new PublishingRoutePreferences
        {
            ControlWeight = 1,
            FundingWeight = 10,
            SpeedWeight = 1,
            DistributionWeight = 10,
            RightsWeight = 1,
            ProductionWeight = 10,
        });
        Assert.Equal(PublishingRoute.Traditional, preferPublisher.RecommendedRoute);
    }

    [Fact]
    public void BetaSynthesizer_GroupsWithoutAiClaims()
    {
        var empty = BetaFeedbackSynthesizer.Synthesize([]);
        Assert.Equal(0, empty.EntryCount);

        var result = BetaFeedbackSynthesizer.Synthesize(
        [
            new BetaFeedbackEntry { Category = "Pacing", Observation = "Slow middle", SuggestedFix = "" },
            new BetaFeedbackEntry { Category = "Pacing", Observation = "Slow middle", SuggestedFix = "Cut chapter 8" },
            new BetaFeedbackEntry { Category = "Clarity", Observation = "Confusing magic", SuggestedFix = "Add rule earlier" },
        ]);

        Assert.Equal(3, result.EntryCount);
        Assert.Equal(1, result.ObservationOnlyCount);
        Assert.Equal(2, result.SuggestionCount);
        Assert.Contains(result.GroupsByCategory, group => group.Key == "Pacing" && group.Count == 2);
        Assert.Contains("not AI", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }
}
