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

    public SceneDraftStatus Status { get; set; } = SceneDraftStatus.Outlined;

    public int WordCount { get; set; }
}
