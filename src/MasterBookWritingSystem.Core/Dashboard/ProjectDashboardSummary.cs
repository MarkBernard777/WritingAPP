using MasterBookWritingSystem.Core.Domain;

namespace MasterBookWritingSystem.Core.Dashboard;

public sealed class ProjectDashboardSummary
{
    public required Guid ProjectId { get; init; }

    public required string Title { get; init; }

    public string Author { get; init; } = string.Empty;

    public string Genre { get; init; } = string.Empty;

    public PublishingRoute PublishingRoute { get; init; }

    public string NorthStar { get; init; } = string.Empty;

    public required string RootPath { get; init; }

    public int ApplicablePhaseCount { get; init; }

    public int ApplicableStepCount { get; init; }

    public int CompletedStepCount { get; init; }

    public int PassedGateCount { get; init; }

    public string? CurrentPhaseId { get; init; }

    public string? CurrentPhaseTitle { get; init; }

    public string CurrentDeliverable { get; init; } = string.Empty;

    public string NextAction { get; init; } = string.Empty;

    public int WordCount { get; init; }

    public DateTimeOffset LastEditedUtc { get; init; }
}
