using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Publishing;
using MasterBookWritingSystem.Core.Publishing;

namespace MasterBookWritingSystem.Tests.Publishing;

public sealed class PublishingValidationTests
{
    [Fact]
    public void Submission_RequiresNameAndValidDates()
    {
        var invalid = PublishingValidation.Validate(new Submission
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = " ",
            RouteAffinity = PublishingRoute.Traditional,
            SentDate = new DateOnly(2026, 8, 10),
            FollowUpDate = new DateOnly(2026, 8, 1),
        });

        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, error => error.Contains("name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(invalid.Errors, error => error.Contains("Follow-up", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RejectsUnsafeRelativePaths()
    {
        Assert.False(ProjectRelativePathRules.IsValid("../escape.txt"));
        Assert.False(ProjectRelativePathRules.IsValid("C:\\abs\\file.txt"));
        Assert.True(ProjectRelativePathRules.IsValid("11 Publishing/contract.pdf"));
    }

    [Fact]
    public void Rights_RejectsNegativePaymentAndBadDateRange()
    {
        var result = PublishingValidation.Validate(new RightsAndContract
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Party = "Agency",
            RightOrService = "Audio",
            Payment = -1,
            StartDate = new DateOnly(2026, 1, 1),
            EndOrReversionDate = new DateOnly(2025, 1, 1),
            AgreementFile = "11 Publishing/deal.pdf",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Payment", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("End or reversion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TraditionalSubmissionsInactiveOnSelfPublishingRoute()
    {
        Assert.False(PublishingValidation.IsSubmissionActiveForRoute(
            PublishingRoute.SelfPublishing,
            PublishingRoute.Traditional));
        Assert.True(PublishingValidation.IsSubmissionActiveForRoute(
            PublishingRoute.Traditional,
            PublishingRoute.Traditional));
    }

    [Fact]
    public void PerformanceAndCorrection_ValidateRanges()
    {
        var performance = PublishingValidation.Validate(new PerformanceRecord
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            PeriodLabel = "Q1",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 1, 1),
            Sales = -1,
        });
        Assert.False(performance.IsValid);

        var correction = PublishingValidation.Validate(new Correction
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Error = "Typo",
            CorrectionText = "Fix",
            ReportedDate = new DateOnly(2026, 8, 10),
            CorrectedDate = new DateOnly(2026, 8, 1),
        });
        Assert.False(correction.IsValid);
    }

    [Fact]
    public void Format_RequiresNameAndRejectsBadAssetPath()
    {
        var result = PublishingValidation.Validate(new PublishingFormat
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = " ",
            AssetLink = "../escape.epub",
            Price = -1,
        });
        Assert.False(result.IsValid);
    }
}
