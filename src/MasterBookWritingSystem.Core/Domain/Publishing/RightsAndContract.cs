namespace MasterBookWritingSystem.Core.Domain.Publishing;

public enum RightsContractStatus
{
    Draft = 0,
    Pending = 1,
    Active = 2,
    Expired = 3,
    Terminated = 4,
}

/// <summary>Rights / contracts register row (Rights_Contracts.csv shape).</summary>
public sealed class RightsAndContract
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Party { get; set; }

    public required string RightOrService { get; set; }

    public string Territory { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndOrReversionDate { get; set; }

    /// <summary>Payment amount when numeric; free-text notes also allowed via PaymentNotes.</summary>
    public decimal? Payment { get; set; }

    public string PaymentNotes { get; set; } = string.Empty;

    public string Restrictions { get; set; } = string.Empty;

    /// <summary>Project-relative path to the agreement file.</summary>
    public string AgreementFile { get; set; } = string.Empty;

    public RightsContractStatus Status { get; set; } = RightsContractStatus.Draft;

    public DateTimeOffset? LastEditedUtc { get; set; }
}
