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
