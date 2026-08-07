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

    PublishingValidationResult ValidateSubmission(Submission submission);

    PublishingValidationResult ValidateLaunchItem(LaunchItem item);

    PublishingValidationResult ValidateRightsAndContract(RightsAndContract contract);

    /// <summary>
    /// Resolves a project-relative link under the project root.
    /// Returns null when the path is empty, an external URL, or the file/folder is missing.
    /// </summary>
    string? ResolveLocalLink(string projectRoot, string? relativeOrUrl, out string? missingMessage);
}
