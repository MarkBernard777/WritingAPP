using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Publishing;

namespace MasterBookWritingSystem.Core.Publishing;

public sealed class PublishingValidationResult
{
    public required bool IsValid { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public static PublishingValidationResult Success() => new() { IsValid = true };

    public static PublishingValidationResult Failure(params string[] errors)
        => new() { IsValid = false, Errors = errors };
}

public static class PublishingValidation
{
    public static PublishingValidationResult Validate(Submission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(submission.Name))
        {
            errors.Add("Submission name is required.");
        }

        if (!Enum.IsDefined(submission.Response))
        {
            errors.Add("Submission response is invalid.");
        }

        if (!Enum.IsDefined(submission.Outcome))
        {
            errors.Add("Submission outcome is invalid.");
        }

        if (!Enum.IsDefined(submission.RouteAffinity)
            || submission.RouteAffinity == PublishingRoute.Unspecified)
        {
            errors.Add("Submission route affinity must be Traditional, SelfPublishing, or Hybrid.");
        }

        if (submission.SentDate is { } sent
            && submission.FollowUpDate is { } followUp
            && followUp < sent)
        {
            errors.Add("Follow-up date cannot be earlier than sent date.");
        }

        ValidateOptionalLink(submission.Website, "Website", errors);
        return Result(errors);
    }

    public static PublishingValidationResult Validate(LaunchItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(item.Asset))
        {
            errors.Add("Launch asset name is required.");
        }

        if (!Enum.IsDefined(item.Status))
        {
            errors.Add("Launch item status is invalid.");
        }

        if (!Enum.IsDefined(item.RouteAffinity))
        {
            errors.Add("Launch item route affinity is invalid.");
        }

        ValidateOptionalLink(item.Link, "Link", errors);
        return Result(errors);
    }

    public static PublishingValidationResult Validate(RightsAndContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(contract.Party))
        {
            errors.Add("Party is required.");
        }

        if (string.IsNullOrWhiteSpace(contract.RightOrService))
        {
            errors.Add("Right or service is required.");
        }

        if (!Enum.IsDefined(contract.Status))
        {
            errors.Add("Rights contract status is invalid.");
        }

        if (contract.Payment is { } payment && payment < 0)
        {
            errors.Add("Payment cannot be negative.");
        }

        if (contract.StartDate is { } start
            && contract.EndOrReversionDate is { } end
            && end < start)
        {
            errors.Add("End or reversion date cannot be earlier than start date.");
        }

        ValidateOptionalLink(contract.AgreementFile, "Agreement file", errors, allowHttp: false);
        return Result(errors);
    }

    public static PublishingValidationResult Validate(PublishingFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(format.Name))
        {
            errors.Add("Format name is required.");
        }

        if (!Enum.IsDefined(format.FormatKind))
        {
            errors.Add("Format kind is invalid.");
        }

        if (!Enum.IsDefined(format.PublicationStatus))
        {
            errors.Add("Publication status is invalid.");
        }

        if (format.Price is { } price && price < 0)
        {
            errors.Add("Price cannot be negative.");
        }

        ValidateOptionalLink(format.AssetLink, "Asset link", errors, allowHttp: false);
        return Result(errors);
    }

    public static PublishingValidationResult Validate(MetadataRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(record.Title))
        {
            errors.Add("Metadata title is required.");
        }

        if (!Enum.IsDefined(record.PublicationStatus))
        {
            errors.Add("Publication status is invalid.");
        }

        return Result(errors);
    }

    public static PublishingValidationResult Validate(PerformanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(record.PeriodLabel))
        {
            errors.Add("Performance period is required.");
        }

        if (record.PeriodStart is { } start
            && record.PeriodEnd is { } end
            && end < start)
        {
            errors.Add("Period end cannot be earlier than period start.");
        }

        if (record.Sales is { } sales && sales < 0)
        {
            errors.Add("Sales cannot be negative.");
        }

        if (record.ReadThrough is { } readThrough && readThrough < 0)
        {
            errors.Add("Read-through cannot be negative.");
        }

        if (record.MailingList is { } mailing && mailing < 0)
        {
            errors.Add("Mailing list count cannot be negative.");
        }

        if (record.Reviews is { } reviews && reviews < 0)
        {
            errors.Add("Reviews cannot be negative.");
        }

        if (record.AdCost is { } adCost && adCost < 0)
        {
            errors.Add("Ad cost cannot be negative.");
        }

        return Result(errors);
    }

    public static PublishingValidationResult Validate(Correction correction)
    {
        ArgumentNullException.ThrowIfNull(correction);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(correction.Error))
        {
            errors.Add("Error description is required.");
        }

        if (string.IsNullOrWhiteSpace(correction.CorrectionText))
        {
            errors.Add("Correction text is required.");
        }

        if (!Enum.IsDefined(correction.Status))
        {
            errors.Add("Correction status is invalid.");
        }

        if (correction.ReportedDate is { } reported
            && correction.CorrectedDate is { } corrected
            && corrected < reported)
        {
            errors.Add("Corrected date cannot be earlier than reported date.");
        }

        return Result(errors);
    }

    public static bool IsSubmissionActiveForRoute(PublishingRoute activeRoute, PublishingRoute submissionRoute)
    {
        if (activeRoute == PublishingRoute.SelfPublishing)
        {
            return submissionRoute != PublishingRoute.Traditional;
        }

        return true;
    }

    private static void ValidateOptionalLink(
        string? value,
        string fieldName,
        List<string> errors,
        bool allowHttp = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (allowHttp
            && (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!ProjectRelativePathRules.IsValid(trimmed))
        {
            errors.Add($"{fieldName} must be a project-relative path"
                + (allowHttp ? " or http(s) URL." : "."));
        }
    }

    private static PublishingValidationResult Result(List<string> errors)
        => errors.Count == 0
            ? PublishingValidationResult.Success()
            : PublishingValidationResult.Failure(errors.ToArray());
}

public static class ProjectRelativePathRules
{
    public static bool IsValid(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.StartsWith('/')
            || normalized.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    public static string Normalize(string path)
        => path.Replace('\\', '/').Trim().TrimStart('/');
}
