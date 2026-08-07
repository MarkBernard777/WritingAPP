namespace MasterBookWritingSystem.Core.Workflow;

public sealed class PhaseGateCompletion
{
    public required string PhaseId { get; init; }

    public bool ConfirmedByUser { get; init; }

    public string Evidence { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public string? OverrideReason { get; init; }

    public DateTimeOffset? PassedUtc { get; init; }
}
