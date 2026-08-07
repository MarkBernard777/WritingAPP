namespace MasterBookWritingSystem.Core.Domain.Publishing;

public enum LaunchItemStatus
{
    Planned = 0,
    Ready = 1,
    InProgress = 2,
    Done = 3,
    Cancelled = 4,
    Skipped = 5,
}

/// <summary>Launch calendar row (Launch_Calendar.csv shape).</summary>
public sealed class LaunchItem
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public DateOnly? Date { get; set; }

    public string Phase { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;

    public required string Asset { get; set; }

    public string Audience { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    /// <summary>Project-relative path or external URL.</summary>
    public string Link { get; set; } = string.Empty;

    public LaunchItemStatus Status { get; set; } = LaunchItemStatus.Planned;

    public string Result { get; set; } = string.Empty;

    /// <summary>Optional route tag for filtering; Unspecified means available on all routes.</summary>
    public PublishingRoute RouteAffinity { get; set; } = PublishingRoute.Unspecified;

    public DateTimeOffset? LastEditedUtc { get; set; }
}
