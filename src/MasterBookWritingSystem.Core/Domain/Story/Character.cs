namespace MasterBookWritingSystem.Core.Domain.Story;

public sealed class Character
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Name { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Goal { get; set; } = string.Empty;

    public string Need { get; set; } = string.Empty;

    public string Fear { get; set; } = string.Empty;

    public string Wound { get; set; } = string.Empty;

    public string FalseBelief { get; set; } = string.Empty;

    public string Contradiction { get; set; } = string.Empty;

    public string Skills { get; set; } = string.Empty;

    public string Weaknesses { get; set; } = string.Empty;

    public string Resources { get; set; } = string.Empty;

    /// <summary>Free-text relationship notes. Structured Relationship rows are deferred.</summary>
    public string RelationshipsNotes { get; set; } = string.Empty;

    public string StartingState { get; set; } = string.Empty;

    public string EndingState { get; set; } = string.Empty;

    public string BookArc { get; set; } = string.Empty;

    public string SeriesArc { get; set; } = string.Empty;

    public bool IsViewpoint { get; set; }

    public string SceneAppearancesNotes { get; set; } = string.Empty;

    public DateTimeOffset? LastEditedUtc { get; set; }
}
