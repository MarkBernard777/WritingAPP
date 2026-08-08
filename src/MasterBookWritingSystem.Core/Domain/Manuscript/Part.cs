namespace MasterBookWritingSystem.Core.Domain.Manuscript;

public sealed class Part
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required Guid BookId { get; init; }

    public required int SequenceNumber { get; set; }

    public required string Title { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }
}
