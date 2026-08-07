using MasterBookWritingSystem.Core.Domain;

namespace MasterBookWritingSystem.Core.Tools;

public sealed class PublishingRouteScores
{
    public required PublishingRoute Route { get; init; }

    public required int Control { get; init; }

    public required int Funding { get; init; }

    public required int Speed { get; init; }

    public required int Distribution { get; init; }

    public required int Rights { get; init; }

    public required int Production { get; init; }

    public int Total => Control + Funding + Speed + Distribution + Rights + Production;
}

public sealed class PublishingRouteDecisionResult
{
    public required PublishingRoute RecommendedRoute { get; init; }

    public required IReadOnlyList<PublishingRouteScores> Breakdown { get; init; }

    public required string Explanation { get; init; }
}

public sealed class PublishingRoutePreferences
{
    /// <summary>0–10 importance weights for each dimension.</summary>
    public int ControlWeight { get; set; } = 5;

    public int FundingWeight { get; set; } = 5;

    public int SpeedWeight { get; set; } = 5;

    public int DistributionWeight { get; set; } = 5;

    public int RightsWeight { get; set; } = 5;

    public int ProductionWeight { get; set; } = 5;
}

public static class PublishingRouteDecisionCalculator
{
    // Base capability scores (0–10) for each route on each dimension.
    private static readonly Dictionary<PublishingRoute, int[]> BaseScores = new()
    {
        // control, funding, speed, distribution, rights, production
        [PublishingRoute.Traditional] = [3, 8, 3, 8, 4, 8],
        [PublishingRoute.SelfPublishing] = [9, 4, 8, 6, 9, 4],
        [PublishingRoute.Hybrid] = [6, 6, 6, 7, 6, 6],
    };

    public static PublishingRouteDecisionResult Decide(PublishingRoutePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var weights = new[]
        {
            Clamp(preferences.ControlWeight),
            Clamp(preferences.FundingWeight),
            Clamp(preferences.SpeedWeight),
            Clamp(preferences.DistributionWeight),
            Clamp(preferences.RightsWeight),
            Clamp(preferences.ProductionWeight),
        };

        var breakdown = new List<PublishingRouteScores>();
        foreach (var route in new[] { PublishingRoute.Traditional, PublishingRoute.SelfPublishing, PublishingRoute.Hybrid })
        {
            var bases = BaseScores[route];
            breakdown.Add(new PublishingRouteScores
            {
                Route = route,
                Control = bases[0] * weights[0],
                Funding = bases[1] * weights[1],
                Speed = bases[2] * weights[2],
                Distribution = bases[3] * weights[3],
                Rights = bases[4] * weights[4],
                Production = bases[5] * weights[5],
            });
        }

        var recommended = breakdown.OrderByDescending(item => item.Total).ThenBy(item => item.Route).First();
        return new PublishingRouteDecisionResult
        {
            RecommendedRoute = recommended.Route,
            Breakdown = breakdown,
            Explanation =
                "Each route has fixed capability scores (0–10) for control, funding, speed, distribution, rights and production. "
                + "Your importance weights (0–10) multiply those scores. Highest weighted total wins. "
                + "Applying a route only filters workflow visibility; inactive-route step and gate data are preserved.",
        };
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 10);
}
