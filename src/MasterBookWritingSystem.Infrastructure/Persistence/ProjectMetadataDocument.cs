using System.Text.Json.Serialization;
using MasterBookWritingSystem.Core.Domain;

namespace MasterBookWritingSystem.Infrastructure.Persistence;

public sealed class ProjectMetadataDocument
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("genre")]
    public string Genre { get; set; } = string.Empty;

    [JsonPropertyName("publishingRoute")]
    public PublishingRoute PublishingRoute { get; set; }

    [JsonPropertyName("northStar")]
    public string NorthStar { get; set; } = string.Empty;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; set; }

    [JsonPropertyName("lastEditedUtc")]
    public DateTimeOffset LastEditedUtc { get; set; }
}
