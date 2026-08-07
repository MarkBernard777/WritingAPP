namespace MasterBookWritingSystem.Core.Domain.Story;

public enum SceneDraftStatus
{
    Outlined = 0,
    Drafting = 1,
    Drafted = 2,
    Revising = 3,
    Complete = 4,
}

public sealed class Scene
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public Guid? ChapterId { get; set; }

    public required int SequenceNumber { get; set; }

    public required string Title { get; set; }

    public Guid? ViewpointCharacterId { get; set; }

    public string Location { get; set; } = string.Empty;

    public string Time { get; set; } = string.Empty;

    public string Goal { get; set; } = string.Empty;

    public string Opposition { get; set; } = string.Empty;

    public string Stakes { get; set; } = string.Empty;

    public string MainEvent { get; set; } = string.Empty;

    public string Revelation { get; set; } = string.Empty;

    public string EmotionalTurn { get; set; } = string.Empty;

    public string Choice { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string Consequence { get; set; } = string.Empty;

    /// <summary>Free-text setup obligations. Structured ledgers are deferred.</summary>
    public string SetupObligations { get; set; } = string.Empty;

    /// <summary>Free-text payoff obligations. Structured ledgers are deferred.</summary>
    public string PayoffObligations { get; set; } = string.Empty;

    public Guid? NextSceneId { get; set; }

    public SceneDraftStatus Status { get; set; } = SceneDraftStatus.Outlined;

    public int WordCount { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }
}
