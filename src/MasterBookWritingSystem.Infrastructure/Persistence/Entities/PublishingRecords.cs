using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Publishing;

namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class SubmissionRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string Name { get; set; }

    public string AgencyOrPublisher { get; set; } = string.Empty;

    public string Website { get; set; } = string.Empty;

    public string Fit { get; set; } = string.Empty;

    public string Requirements { get; set; } = string.Empty;

    public string MaterialSent { get; set; } = string.Empty;

    public DateOnly? SentDate { get; set; }

    public DateOnly? FollowUpDate { get; set; }

    public SubmissionResponse Response { get; set; }

    public SubmissionOutcome Outcome { get; set; }

    public PublishingRoute RouteAffinity { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }
}

public sealed class LaunchItemRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public DateOnly? Date { get; set; }

    public string Phase { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;

    public required string Asset { get; set; }

    public string Audience { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public string Link { get; set; } = string.Empty;

    public LaunchItemStatus Status { get; set; }

    public string Result { get; set; } = string.Empty;

    public PublishingRoute RouteAffinity { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }
}

public sealed class RightsAndContractRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string Party { get; set; }

    public required string RightOrService { get; set; }

    public string Territory { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndOrReversionDate { get; set; }

    public decimal? Payment { get; set; }

    public string PaymentNotes { get; set; } = string.Empty;

    public string Restrictions { get; set; } = string.Empty;

    public string AgreementFile { get; set; } = string.Empty;

    public RightsContractStatus Status { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }
}

public sealed class PublishingFormatRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string Name { get; set; }

    public PublishingFormatKind FormatKind { get; set; }

    public string IsbnOrAsin { get; set; } = string.Empty;

    public string TrimOrFileSpec { get; set; } = string.Empty;

    public decimal? Price { get; set; }

    public string Distributor { get; set; } = string.Empty;

    public PublicationStatus PublicationStatus { get; set; }

    public string AssetLink { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset? LastEditedUtc { get; set; }
}

public sealed class MetadataRecordEntity
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

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

    public string FormatName { get; set; } = string.Empty;

    public PublicationStatus PublicationStatus { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }
}

public sealed class PerformanceRecordEntity
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

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

public sealed class CorrectionRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string Error { get; set; }

    public string Location { get; set; } = string.Empty;

    public required string CorrectionText { get; set; }

    public DateOnly? ReportedDate { get; set; }

    public DateOnly? CorrectedDate { get; set; }

    public string FormatsUpdated { get; set; } = string.Empty;

    public string NewEdition { get; set; } = string.Empty;

    public CorrectionStatus Status { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }
}
