namespace MasterBookWritingSystem.Core.Domain.Publishing;

/// <summary>Performance register row (Performance_Register.csv shape).</summary>
public sealed class PerformanceRecord
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>Human-readable period label (e.g. "2026-Q3" or "Launch week").</summary>
    public required string PeriodLabel { get; set; }

    public DateOnly? PeriodStart { get; set; }

    public DateOnly? PeriodEnd { get; set; }

    public string Format { get; set; } = string.Empty;

    public decimal? Sales { get; set; }

    public decimal? ReadThrough { get; set; }

    public int? MailingList { get; set; }

    public int? Reviews { get; set; }

    public decimal? AdCost { get; set; }

    public string Availability { get; set; } = string.Empty;

    public string ReturnsOrIssues { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset? LastEditedUtc { get; set; }
}
