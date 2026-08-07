using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Publishing;
using MasterBookWritingSystem.Core.Publishing;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IPublishingService
{
    Task<IReadOnlyList<Submission>> GetSubmissionsAsync(
        Guid projectId,
        PublishingRoute? activeRoute = null,
        SubmissionOutcome? outcomeFilter = null,
        bool includeInactiveRouteRows = false,
        CancellationToken cancellationToken = default);

    Task<Submission> GetSubmissionAsync(
        Guid projectId,
        Guid submissionId,
        CancellationToken cancellationToken = default);

    Task<Submission> CreateSubmissionAsync(
        Guid projectId,
        Submission submission,
        CancellationToken cancellationToken = default);

    Task<Submission> UpdateSubmissionAsync(
        Guid projectId,
        Submission submission,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes only when <paramref name="confirmed"/> is true (VM/service delete gate).</summary>
    Task DeleteSubmissionAsync(
        Guid projectId,
        Guid submissionId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LaunchItem>> GetLaunchItemsAsync(
        Guid projectId,
        LaunchItemStatus? statusFilter = null,
        PublishingRoute? routeFilter = null,
        bool sortByDateDescending = false,
        CancellationToken cancellationToken = default);

    Task<LaunchItem> GetLaunchItemAsync(
        Guid projectId,
        Guid launchItemId,
        CancellationToken cancellationToken = default);

    Task<LaunchItem> CreateLaunchItemAsync(
        Guid projectId,
        LaunchItem item,
        CancellationToken cancellationToken = default);

    Task<LaunchItem> UpdateLaunchItemAsync(
        Guid projectId,
        LaunchItem item,
        CancellationToken cancellationToken = default);

    Task DeleteLaunchItemAsync(
        Guid projectId,
        Guid launchItemId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RightsAndContract>> GetRightsAndContractsAsync(
        Guid projectId,
        RightsContractStatus? statusFilter = null,
        CancellationToken cancellationToken = default);

    Task<RightsAndContract> GetRightsAndContractAsync(
        Guid projectId,
        Guid contractId,
        CancellationToken cancellationToken = default);

    Task<RightsAndContract> CreateRightsAndContractAsync(
        Guid projectId,
        RightsAndContract contract,
        CancellationToken cancellationToken = default);

    Task<RightsAndContract> UpdateRightsAndContractAsync(
        Guid projectId,
        RightsAndContract contract,
        CancellationToken cancellationToken = default);

    Task DeleteRightsAndContractAsync(
        Guid projectId,
        Guid contractId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PublishingFormat>> GetPublishingFormatsAsync(
        Guid projectId,
        PublicationStatus? statusFilter = null,
        CancellationToken cancellationToken = default);

    Task<PublishingFormat> GetPublishingFormatAsync(
        Guid projectId,
        Guid formatId,
        CancellationToken cancellationToken = default);

    Task<PublishingFormat> CreatePublishingFormatAsync(
        Guid projectId,
        PublishingFormat format,
        CancellationToken cancellationToken = default);

    Task<PublishingFormat> UpdatePublishingFormatAsync(
        Guid projectId,
        PublishingFormat format,
        CancellationToken cancellationToken = default);

    Task DeletePublishingFormatAsync(
        Guid projectId,
        Guid formatId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MetadataRecord>> GetMetadataRecordsAsync(
        Guid projectId,
        PublicationStatus? statusFilter = null,
        CancellationToken cancellationToken = default);

    Task<MetadataRecord> GetMetadataRecordAsync(
        Guid projectId,
        Guid metadataId,
        CancellationToken cancellationToken = default);

    Task<MetadataRecord> CreateMetadataRecordAsync(
        Guid projectId,
        MetadataRecord record,
        CancellationToken cancellationToken = default);

    Task<MetadataRecord> UpdateMetadataRecordAsync(
        Guid projectId,
        MetadataRecord record,
        CancellationToken cancellationToken = default);

    Task DeleteMetadataRecordAsync(
        Guid projectId,
        Guid metadataId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PerformanceRecord>> GetPerformanceRecordsAsync(
        Guid projectId,
        string? formatFilter = null,
        bool sortByPeriodDescending = true,
        CancellationToken cancellationToken = default);

    Task<PerformanceRecord> GetPerformanceRecordAsync(
        Guid projectId,
        Guid performanceId,
        CancellationToken cancellationToken = default);

    Task<PerformanceRecord> CreatePerformanceRecordAsync(
        Guid projectId,
        PerformanceRecord record,
        CancellationToken cancellationToken = default);

    Task<PerformanceRecord> UpdatePerformanceRecordAsync(
        Guid projectId,
        PerformanceRecord record,
        CancellationToken cancellationToken = default);

    Task DeletePerformanceRecordAsync(
        Guid projectId,
        Guid performanceId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Correction>> GetCorrectionsAsync(
        Guid projectId,
        CorrectionStatus? statusFilter = null,
        bool sortByReportedDescending = true,
        CancellationToken cancellationToken = default);

    Task<Correction> GetCorrectionAsync(
        Guid projectId,
        Guid correctionId,
        CancellationToken cancellationToken = default);

    Task<Correction> CreateCorrectionAsync(
        Guid projectId,
        Correction correction,
        CancellationToken cancellationToken = default);

    Task<Correction> UpdateCorrectionAsync(
        Guid projectId,
        Correction correction,
        CancellationToken cancellationToken = default);

    Task DeleteCorrectionAsync(
        Guid projectId,
        Guid correctionId,
        bool confirmed,
        CancellationToken cancellationToken = default);

    PublishingValidationResult ValidateSubmission(Submission submission);

    PublishingValidationResult ValidateLaunchItem(LaunchItem item);

    PublishingValidationResult ValidateRightsAndContract(RightsAndContract contract);

    PublishingValidationResult ValidatePublishingFormat(PublishingFormat format);

    PublishingValidationResult ValidateMetadataRecord(MetadataRecord record);

    PublishingValidationResult ValidatePerformanceRecord(PerformanceRecord record);

    PublishingValidationResult ValidateCorrection(Correction correction);

    /// <summary>
    /// Resolves a project-relative link under the project root.
    /// Returns null when the path is empty, an external URL, or the file/folder is missing.
    /// </summary>
    string? ResolveLocalLink(string projectRoot, string? relativeOrUrl, out string? missingMessage);
}
