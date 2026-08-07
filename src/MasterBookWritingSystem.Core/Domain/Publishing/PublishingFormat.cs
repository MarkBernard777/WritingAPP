namespace MasterBookWritingSystem.Core.Domain.Publishing;

public enum PublishingFormatKind
{
    Ebook = 0,
    Paperback = 1,
    Hardcover = 2,
    Audiobook = 3,
    Other = 4,
}

public enum PublicationStatus
{
    Planned = 0,
    InProduction = 1,
    Available = 2,
    Unavailable = 3,
    Retired = 4,
}

/// <summary>Published format / edition row derived from Metadata Sheet format fields.</summary>
public sealed class PublishingFormat
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Name { get; set; }

    public PublishingFormatKind FormatKind { get; set; } = PublishingFormatKind.Ebook;

    public string IsbnOrAsin { get; set; } = string.Empty;

    public string TrimOrFileSpec { get; set; } = string.Empty;

    public decimal? Price { get; set; }

    public string Distributor { get; set; } = string.Empty;

    public PublicationStatus PublicationStatus { get; set; } = PublicationStatus.Planned;

    /// <summary>Project-relative path to the format asset (ebook, print file, etc.).</summary>
    public string AssetLink { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset? LastEditedUtc { get; set; }
}
