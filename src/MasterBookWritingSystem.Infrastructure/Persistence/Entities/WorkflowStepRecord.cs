using MasterBookWritingSystem.Core.Domain;

namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class WorkflowStepRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string PhaseId { get; set; }

    public int Number { get; set; }

    public required string Title { get; set; }

    public string ContentJson { get; set; } = "[]";

    public PublishingRoute? RouteAffinity { get; set; }

    public WorkflowPhaseRecord? Phase { get; set; }
}
