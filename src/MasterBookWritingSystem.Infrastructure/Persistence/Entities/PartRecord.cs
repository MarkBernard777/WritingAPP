namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class PartRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid BookId { get; set; }

    public int SequenceNumber { get; set; }

    public required string Title { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }

    public BookRecord? Book { get; set; }

    public List<ChapterRecord> Chapters { get; set; } = [];
}
