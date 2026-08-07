using MasterBookWritingSystem.Core.Domain.Story;

namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class CharacterRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

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

    public string RelationshipsNotes { get; set; } = string.Empty;

    public string StartingState { get; set; } = string.Empty;

    public string EndingState { get; set; } = string.Empty;

    public string BookArc { get; set; } = string.Empty;

    public string SeriesArc { get; set; } = string.Empty;

    public bool IsViewpoint { get; set; }

    public string SceneAppearancesNotes { get; set; } = string.Empty;

    public DateTimeOffset? LastEditedUtc { get; set; }
}

public sealed class WorldEntryRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string Name { get; set; }

    public string Category { get; set; } = string.Empty;

    public WorldDepth Depth { get; set; } = WorldDepth.OnPage;

    public string Notes { get; set; } = string.Empty;

    public string TravelDistance { get; set; } = string.Empty;

    public string TravelTime { get; set; } = string.Empty;

    public string CanonicalFacts { get; set; } = string.Empty;

    public string ConflictingEntries { get; set; } = string.Empty;

    public DateTimeOffset? LastEditedUtc { get; set; }
}

public sealed class BeatRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public int Number { get; set; }

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

public sealed class SceneRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid? ChapterId { get; set; }

    public int SequenceNumber { get; set; }

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

    public string SetupObligations { get; set; } = string.Empty;

    public string PayoffObligations { get; set; } = string.Empty;

    public Guid? NextSceneId { get; set; }

    public SceneDraftStatus Status { get; set; } = SceneDraftStatus.Outlined;

    public int WordCount { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }
}
