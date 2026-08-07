using MasterBookWritingSystem.Core.Domain.Workflow;

namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class StepProgressRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string PhaseId { get; set; }

    public int StepNumber { get; set; }

    public StepStatus Status { get; set; } = StepStatus.NotStarted;

    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset? CompletedUtc { get; set; }
}
