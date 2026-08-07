namespace MasterBookWritingSystem.Core.Domain.Story;

public enum BeatStatus
{
    Planned = 0,
    Drafted = 1,
    Revised = 2,
    Locked = 3,
}

public sealed class Beat
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>Beat number within the structural framework (typically 1–29).</summary>
    public required int Number { get; set; }

    public required string Name { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string ChapterRef { get; set; } = string.Empty;

    public string SceneRef { get; set; } = string.Empty;

    public Guid? ViewpointCharacterId { get; set; }

    public string Event { get; set; } = string.Empty;

    public string Cause { get; set; } = string.Empty;

    public string Consequence { get; set; } = string.Empty;

    public string ArcFunction { get; set; } = string.Empty;

    public string Theme { get; set; } = string.Empty;

    public string Escalation { get; set; } = string.Empty;

    public BeatStatus Status { get; set; } = BeatStatus.Planned;

    public DateTimeOffset? LastEditedUtc { get; set; }
}
