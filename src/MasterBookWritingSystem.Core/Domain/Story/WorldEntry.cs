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

    public string Category { get; set; } = string.Empty;

    public WorldDepth Depth { get; set; } = WorldDepth.OnPage;

    public string Notes { get; set; } = string.Empty;
}
