namespace MasterBookWritingSystem.Core.Domain.Workflow;

public sealed class WorkflowPhase
{
    public required string Id { get; init; }

    public required string Title { get; set; }

    public string Deliverable { get; set; } = string.Empty;

    public string GateStatement { get; set; } = string.Empty;

    public string? TemplatePath { get; set; }

    public PublishingRoute? RouteAffinity { get; set; }

    public IList<WorkflowStep> Steps { get; init; } = [];
}
