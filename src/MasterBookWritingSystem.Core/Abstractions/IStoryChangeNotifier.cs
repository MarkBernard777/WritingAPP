namespace MasterBookWritingSystem.Core.Abstractions;

public enum StoryChangeKind
{
    SceneUpserted = 0,
    SceneDeleted = 1,
    HierarchyChanged = 2,
    CharacterChanged = 3,
    WorldEntryChanged = 4,
    BeatChanged = 5,
}

public sealed class StoryChangeEventArgs : EventArgs
{
    public required Guid ProjectId { get; init; }

    public required StoryChangeKind Kind { get; init; }

    public Guid? EntityId { get; init; }
}

/// <summary>
/// In-process, project-scoped change bus so Manuscript, Story Data and Documents
/// refresh from the same canonical SQLite records without coupling view-models.
/// </summary>
public interface IStoryChangeNotifier
{
    event EventHandler<StoryChangeEventArgs>? Changed;

    void Publish(StoryChangeEventArgs change);
}
