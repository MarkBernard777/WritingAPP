namespace MasterBookWritingSystem.Core.Domain.Story;

public sealed class Character
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Name { get; set; }

    public string Goal { get; set; } = string.Empty;

    public string Need { get; set; } = string.Empty;

    public string Fear { get; set; } = string.Empty;

    public string Wound { get; set; } = string.Empty;

    public string FalseBelief { get; set; } = string.Empty;
}
