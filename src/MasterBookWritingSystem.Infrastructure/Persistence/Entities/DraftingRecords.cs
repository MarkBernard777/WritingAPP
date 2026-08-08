namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class DraftingTargetRecord
{
    public Guid ProjectId { get; set; }

    public int DailyWordGoal { get; set; }

    public int WeeklyWordGoal { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public ProjectRecord? Project { get; set; }
}

public sealed class DraftingSessionRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? BookId { get; set; }

    public Guid? PartId { get; set; }

    public Guid? ChapterId { get; set; }

    public Guid? SceneId { get; set; }

    public Guid? ViewpointCharacterId { get; set; }

    public string? ViewpointLabel { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset? EndedUtc { get; set; }

    public DateTimeOffset LastActivityUtc { get; set; }

    public double ActiveDurationSeconds { get; set; }

    public int StartingWordCount { get; set; }

    public int EndingWordCount { get; set; }

    public int NetWordChange { get; set; }

    public int CompletionReason { get; set; }

    public ProjectRecord? Project { get; set; }
}
