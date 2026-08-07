using MasterBookWritingSystem.Core.Domain;

namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class WorkflowPhaseRecord
{
    public required string Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string Title { get; set; }

    public string Deliverable { get; set; } = string.Empty;

    public string GateStatement { get; set; } = string.Empty;

    public string? TemplatePath { get; set; }

    public PublishingRoute? RouteAffinity { get; set; }

    public int SortOrder { get; set; }

    public ProjectRecord? Project { get; set; }

    public ICollection<WorkflowStepRecord> Steps { get; set; } = [];
}
