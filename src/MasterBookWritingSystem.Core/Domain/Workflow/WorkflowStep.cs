namespace MasterBookWritingSystem.Core.Domain.Workflow;

public sealed class WorkflowStep
{
    public required int Number { get; init; }

    public required string Title { get; set; }

    public required string PhaseId { get; init; }

    public PublishingRoute? RouteAffinity { get; set; }
}
