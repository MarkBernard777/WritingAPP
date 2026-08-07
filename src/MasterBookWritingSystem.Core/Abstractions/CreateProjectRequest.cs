namespace MasterBookWritingSystem.Core.Abstractions;

public sealed class CreateProjectRequest
{
    public required string ParentDirectory { get; init; }

    public required string Title { get; init; }

    public string Author { get; init; } = string.Empty;

    public string Genre { get; init; } = string.Empty;

    public string NorthStar { get; init; } = string.Empty;
}
