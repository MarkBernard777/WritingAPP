namespace MasterBookWritingSystem.Core.Domain.Publishing;

public enum CorrectionStatus
{
    Open = 0,
    InProgress = 1,
    Corrected = 2,
    WonNotFix = 3,
}

/// <summary>Post-publication correction row (Correction_Register.csv shape).</summary>
public sealed class Correction
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Error { get; set; }

    public string Location { get; set; } = string.Empty;

    public required string CorrectionText { get; set; }

    public DateOnly? ReportedDate { get; set; }

    public DateOnly? CorrectedDate { get; set; }

    public string FormatsUpdated { get; set; } = string.Empty;

    public string NewEdition { get; set; } = string.Empty;

    public CorrectionStatus Status { get; set; } = CorrectionStatus.Open;

    public DateTimeOffset? LastEditedUtc { get; set; }
}
