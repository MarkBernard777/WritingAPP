namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class SchemaVersionRecord
{
    public int Version { get; set; }

    public required string Name { get; set; }

    public DateTimeOffset AppliedUtc { get; set; }
}
