namespace MasterBookWritingSystem.Core.Domain.Story;

public enum WorldDepth
{
    OnPage = 0,
    AuthorOnly = 1,
    FutureExpansion = 2,
}

public sealed class WorldEntry
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Name { get; set; }

    /// <summary>
    /// Category such as Location, Culture, Government, Faction, Economics,
    /// Religion, History, or Magic — free text so later typed tables can migrate.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    public WorldDepth Depth { get; set; } = WorldDepth.OnPage;

    public string Notes { get; set; } = string.Empty;

    public string TravelDistance { get; set; } = string.Empty;

    public string TravelTime { get; set; } = string.Empty;

    public string CanonicalFacts { get; set; } = string.Empty;

    public string ConflictingEntries { get; set; } = string.Empty;

    public DateTimeOffset? LastEditedUtc { get; set; }
}
