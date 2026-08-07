namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class PhaseGateRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string PhaseId { get; set; }

    public bool IsPassed { get; set; }

    public bool ConfirmedByUser { get; set; }

    public string Evidence { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public string? OverrideReason { get; set; }

    public DateTimeOffset? PassedUtc { get; set; }
}
