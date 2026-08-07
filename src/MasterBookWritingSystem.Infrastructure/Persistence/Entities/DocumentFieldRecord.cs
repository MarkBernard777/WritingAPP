namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class DocumentFieldRecord
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public Guid ProjectId { get; set; }

    public required string Key { get; set; }

    public required string Label { get; set; }

    public string Value { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }

    public DocumentRecord? Document { get; set; }
}
