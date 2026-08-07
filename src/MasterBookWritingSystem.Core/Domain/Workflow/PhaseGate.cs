namespace MasterBookWritingSystem.Core.Domain.Workflow;

public sealed class PhaseGate
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string PhaseId { get; init; }

    public bool IsPassed { get; set; }

    public bool ConfirmedByUser { get; set; }

    public string? OverrideReason { get; set; }

    public DateTimeOffset? PassedUtc { get; set; }
}
