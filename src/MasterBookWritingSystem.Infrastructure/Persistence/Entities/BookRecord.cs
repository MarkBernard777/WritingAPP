namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class BookRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public int SequenceNumber { get; set; }

    public required string Title { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }

    public ProjectRecord? Project { get; set; }

    public List<PartRecord> Parts { get; set; } = [];
}
