namespace MasterBookWritingSystem.Core.Abstractions;

public sealed class ProjectValidationResult
{
    public required bool IsValid { get; init; }

    public int SchemaVersion { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];
}
