namespace MasterBookWritingSystem.Core.Domain.Workflow;

public sealed class StepProgress
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string PhaseId { get; init; }

    public required int StepNumber { get; init; }

    public StepStatus Status { get; set; } = StepStatus.NotStarted;

    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset? CompletedUtc { get; set; }
}
