namespace MasterBookWritingSystem.Core.Domain.Story;

public sealed class Beat
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required int Number { get; set; }

    public required string Name { get; set; }

    public string Summary { get; set; } = string.Empty;
}
