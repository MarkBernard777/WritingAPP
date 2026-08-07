namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class ChapterRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public int SequenceNumber { get; set; }

    public required string Title { get; set; }

    public required string RelativeMarkdownPath { get; set; }

    public int WordCount { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public ProjectRecord? Project { get; set; }
}
