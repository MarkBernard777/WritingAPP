namespace MasterBookWritingSystem.Core.Domain.Manuscript;

public sealed class Chapter
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required int SequenceNumber { get; set; }

    public required string Title { get; set; }

    public required string RelativeMarkdownPath { get; set; }

    public int WordCount { get; set; }
}
