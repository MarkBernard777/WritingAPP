namespace MasterBookWritingSystem.Core.Domain.Publishing;

public enum SubmissionResponse
{
    None = 0,
    Queried = 1,
    RequestedPartial = 2,
    RequestedFull = 3,
    Rejected = 4,
    Offer = 5,
    NoResponse = 6,
}

public enum SubmissionOutcome
{
    Pending = 0,
    Passed = 1,
    PartialRequest = 2,
    FullRequest = 3,
    Offer = 4,
    Signed = 5,
    Withdrawn = 6,
}

/// <summary>Traditional / hybrid submission tracking row (Submission_Register.csv shape).</summary>
public sealed class Submission
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>Contact or opportunity name.</summary>
    public required string Name { get; set; }

    public string AgencyOrPublisher { get; set; } = string.Empty;

    /// <summary>External URL or project-relative path.</summary>
    public string Website { get; set; } = string.Empty;

    public string Fit { get; set; } = string.Empty;

    public string Requirements { get; set; } = string.Empty;

    public string MaterialSent { get; set; } = string.Empty;

    public DateOnly? SentDate { get; set; }

    public DateOnly? FollowUpDate { get; set; }

    public SubmissionResponse Response { get; set; } = SubmissionResponse.None;

    public SubmissionOutcome Outcome { get; set; } = SubmissionOutcome.Pending;

    /// <summary>
    /// Route this row belongs to. Traditional submissions are hidden (not deleted)
    /// when the active project route is SelfPublishing.
    /// </summary>
    public PublishingRoute RouteAffinity { get; set; } = PublishingRoute.Traditional;

    public DateTimeOffset? LastEditedUtc { get; set; }
}
