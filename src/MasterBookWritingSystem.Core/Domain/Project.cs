namespace MasterBookWritingSystem.Core.Domain;

public sealed class Project
{
    public required Guid Id { get; init; }

    public required string Title { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Genre { get; set; } = string.Empty;

    public PublishingRoute PublishingRoute { get; set; } = PublishingRoute.Unspecified;

    public string NorthStar { get; set; } = string.Empty;

    public required string RootPath { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset LastEditedUtc { get; set; }
}
