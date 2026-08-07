namespace MasterBookWritingSystem.Core.Domain.Publishing;

/// <summary>Metadata Sheet snapshot row (one per edition/format listing).</summary>
public sealed class MetadataRecord
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Title { get; set; }

    public string Subtitle { get; set; } = string.Empty;

    public string Series { get; set; } = string.Empty;

    public string SeriesNumber { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Categories { get; set; } = string.Empty;

    public string SearchTerms { get; set; } = string.Empty;

    public string ReaderAge { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public DateOnly? PublicationDate { get; set; }

    public string Edition { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public string PricingNotes { get; set; } = string.Empty;

    public string TerritoryRights { get; set; } = string.Empty;

    public string Isbn { get; set; } = string.Empty;

    /// <summary>Loose format name (avoids FK cascade complexity with PublishingFormats).</summary>
    public string FormatName { get; set; } = string.Empty;

    public PublicationStatus PublicationStatus { get; set; } = PublicationStatus.Planned;

    public DateTimeOffset? LastEditedUtc { get; set; }
}
