using MasterBookWritingSystem.Core.Domain;

namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class ProjectRecord
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Genre { get; set; } = string.Empty;

    public PublishingRoute PublishingRoute { get; set; } = PublishingRoute.Unspecified;

    public string NorthStar { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset LastEditedUtc { get; set; }

    public ICollection<ChapterRecord> Chapters { get; set; } = [];
}
