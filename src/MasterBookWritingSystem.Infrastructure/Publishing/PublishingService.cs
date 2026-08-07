using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Publishing;
using MasterBookWritingSystem.Core.Publishing;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Publishing;

public sealed class PublishingService : IPublishingService
{
    private readonly IProjectService _projectService;

    public PublishingService(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public PublishingValidationResult ValidateSubmission(Submission submission)
        => PublishingValidation.Validate(submission);

    public PublishingValidationResult ValidateLaunchItem(LaunchItem item)
        => PublishingValidation.Validate(item);

    public PublishingValidationResult ValidateRightsAndContract(RightsAndContract contract)
        => PublishingValidation.Validate(contract);

    public PublishingValidationResult ValidatePublishingFormat(PublishingFormat format)
        => PublishingValidation.Validate(format);

    public PublishingValidationResult ValidateMetadataRecord(MetadataRecord record)
        => PublishingValidation.Validate(record);

    public PublishingValidationResult ValidatePerformanceRecord(PerformanceRecord record)
        => PublishingValidation.Validate(record);

    public PublishingValidationResult ValidateCorrection(Correction correction)
        => PublishingValidation.Validate(correction);

    public async Task<IReadOnlyList<Submission>> GetSubmissionsAsync(
        Guid projectId,
        PublishingRoute? activeRoute = null,
        SubmissionOutcome? outcomeFilter = null,
        bool includeInactiveRouteRows = false,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var query = context.Submissions
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId);

        if (outcomeFilter is { } outcome)
        {
            query = query.Where(item => item.Outcome == outcome);
        }

        var records = await query
            .OrderByDescending(item => item.SentDate)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();

        IEnumerable<SubmissionRecord> filtered = records;
        if (!includeInactiveRouteRows && activeRoute is { } route)
        {
            filtered = records.Where(item =>
                PublishingValidation.IsSubmissionActiveForRoute(route, item.RouteAffinity));
        }

        return filtered.Select(ToDomain).ToList();
    }

    public async Task<Submission> GetSubmissionAsync(
        Guid projectId,
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.Submissions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == submissionId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Submission '{submissionId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Submission> CreateSubmissionAsync(
        Guid projectId,
        Submission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        EnsureValid(ValidateSubmission(submission));
        NormalizeSubmission(submission);

        await using var context = Open(projectId);
        var record = ToRecord(submission, projectId);
        if (record.Id == Guid.Empty)
        {
            record.Id = Guid.NewGuid();
        }

        record.ProjectId = projectId;
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        context.Submissions.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Submission> UpdateSubmissionAsync(
        Guid projectId,
        Submission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        EnsureValid(ValidateSubmission(submission));
        NormalizeSubmission(submission);

        await using var context = Open(projectId);
        var record = await context.Submissions
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == submission.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Submission '{submission.Id}' was not found.");

        Apply(record, submission);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task DeleteSubmissionAsync(
        Guid projectId,
        Guid submissionId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureConfirmed(confirmed);
        await using var context = Open(projectId);
        var record = await context.Submissions
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == submissionId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Submission '{submissionId}' was not found.");
        context.Submissions.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<LaunchItem>> GetLaunchItemsAsync(
        Guid projectId,
        LaunchItemStatus? statusFilter = null,
        PublishingRoute? routeFilter = null,
        bool sortByDateDescending = false,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var query = context.LaunchItems
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId);

        if (statusFilter is { } status)
        {
            query = query.Where(item => item.Status == status);
        }

        if (routeFilter is { } route && route != PublishingRoute.Unspecified)
        {
            query = query.Where(item =>
                item.RouteAffinity == PublishingRoute.Unspecified
                || item.RouteAffinity == route);
        }

        query = sortByDateDescending
            ? query.OrderByDescending(item => item.Date).ThenBy(item => item.Asset)
            : query.OrderBy(item => item.Date).ThenBy(item => item.Asset);

        var records = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<LaunchItem> GetLaunchItemAsync(
        Guid projectId,
        Guid launchItemId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.LaunchItems
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == launchItemId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Launch item '{launchItemId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<LaunchItem> CreateLaunchItemAsync(
        Guid projectId,
        LaunchItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureValid(ValidateLaunchItem(item));
        NormalizeLaunchItem(item);

        await using var context = Open(projectId);
        var record = ToRecord(item, projectId);
        if (record.Id == Guid.Empty)
        {
            record.Id = Guid.NewGuid();
        }

        record.ProjectId = projectId;
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        context.LaunchItems.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<LaunchItem> UpdateLaunchItemAsync(
        Guid projectId,
        LaunchItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureValid(ValidateLaunchItem(item));
        NormalizeLaunchItem(item);

        await using var context = Open(projectId);
        var record = await context.LaunchItems
            .FirstOrDefaultAsync(
                itemRow => itemRow.ProjectId == projectId && itemRow.Id == item.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Launch item '{item.Id}' was not found.");

        Apply(record, item);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task DeleteLaunchItemAsync(
        Guid projectId,
        Guid launchItemId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureConfirmed(confirmed);
        await using var context = Open(projectId);
        var record = await context.LaunchItems
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == launchItemId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Launch item '{launchItemId}' was not found.");
        context.LaunchItems.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<RightsAndContract>> GetRightsAndContractsAsync(
        Guid projectId,
        RightsContractStatus? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var query = context.RightsAndContracts
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId);

        if (statusFilter is { } status)
        {
            query = query.Where(item => item.Status == status);
        }

        var records = await query
            .OrderBy(item => item.Party)
            .ThenBy(item => item.RightOrService)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<RightsAndContract> GetRightsAndContractAsync(
        Guid projectId,
        Guid contractId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.RightsAndContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == contractId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Rights/contract '{contractId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<RightsAndContract> CreateRightsAndContractAsync(
        Guid projectId,
        RightsAndContract contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        EnsureValid(ValidateRightsAndContract(contract));
        NormalizeContract(contract);

        await using var context = Open(projectId);
        var record = ToRecord(contract, projectId);
        if (record.Id == Guid.Empty)
        {
            record.Id = Guid.NewGuid();
        }

        record.ProjectId = projectId;
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        context.RightsAndContracts.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<RightsAndContract> UpdateRightsAndContractAsync(
        Guid projectId,
        RightsAndContract contract,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        EnsureValid(ValidateRightsAndContract(contract));
        NormalizeContract(contract);

        await using var context = Open(projectId);
        var record = await context.RightsAndContracts
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == contract.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Rights/contract '{contract.Id}' was not found.");

        Apply(record, contract);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task DeleteRightsAndContractAsync(
        Guid projectId,
        Guid contractId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureConfirmed(confirmed);
        await using var context = Open(projectId);
        var record = await context.RightsAndContracts
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == contractId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Rights/contract '{contractId}' was not found.");
        context.RightsAndContracts.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<PublishingFormat>> GetPublishingFormatsAsync(
        Guid projectId,
        PublicationStatus? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var query = context.PublishingFormats
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId);

        if (statusFilter is { } status)
        {
            query = query.Where(item => item.PublicationStatus == status);
        }

        var records = await query
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<PublishingFormat> GetPublishingFormatAsync(
        Guid projectId,
        Guid formatId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.PublishingFormats
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == formatId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Publishing format '{formatId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<PublishingFormat> CreatePublishingFormatAsync(
        Guid projectId,
        PublishingFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);
        EnsureValid(ValidatePublishingFormat(format));
        NormalizeFormat(format);

        await using var context = Open(projectId);
        var record = ToRecord(format, projectId);
        if (record.Id == Guid.Empty)
        {
            record.Id = Guid.NewGuid();
        }

        record.ProjectId = projectId;
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        context.PublishingFormats.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<PublishingFormat> UpdatePublishingFormatAsync(
        Guid projectId,
        PublishingFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(format);
        EnsureValid(ValidatePublishingFormat(format));
        NormalizeFormat(format);

        await using var context = Open(projectId);
        var record = await context.PublishingFormats
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == format.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Publishing format '{format.Id}' was not found.");

        Apply(record, format);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task DeletePublishingFormatAsync(
        Guid projectId,
        Guid formatId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureConfirmed(confirmed);
        await using var context = Open(projectId);
        var record = await context.PublishingFormats
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == formatId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Publishing format '{formatId}' was not found.");
        context.PublishingFormats.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<MetadataRecord>> GetMetadataRecordsAsync(
        Guid projectId,
        PublicationStatus? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var query = context.MetadataRecords
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId);

        if (statusFilter is { } status)
        {
            query = query.Where(item => item.PublicationStatus == status);
        }

        var records = await query
            .OrderBy(item => item.Title)
            .ThenBy(item => item.Edition)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<MetadataRecord> GetMetadataRecordAsync(
        Guid projectId,
        Guid metadataId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.MetadataRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == metadataId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Metadata record '{metadataId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<MetadataRecord> CreateMetadataRecordAsync(
        Guid projectId,
        MetadataRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureValid(ValidateMetadataRecord(record));
        NormalizeMetadata(record);

        await using var context = Open(projectId);
        var entity = ToRecord(record, projectId);
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        entity.ProjectId = projectId;
        entity.LastEditedUtc = DateTimeOffset.UtcNow;
        context.MetadataRecords.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(entity);
    }

    public async Task<MetadataRecord> UpdateMetadataRecordAsync(
        Guid projectId,
        MetadataRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureValid(ValidateMetadataRecord(record));
        NormalizeMetadata(record);

        await using var context = Open(projectId);
        var entity = await context.MetadataRecords
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == record.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Metadata record '{record.Id}' was not found.");

        Apply(entity, record);
        entity.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(entity);
    }

    public async Task DeleteMetadataRecordAsync(
        Guid projectId,
        Guid metadataId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureConfirmed(confirmed);
        await using var context = Open(projectId);
        var entity = await context.MetadataRecords
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == metadataId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Metadata record '{metadataId}' was not found.");
        context.MetadataRecords.Remove(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<PerformanceRecord>> GetPerformanceRecordsAsync(
        Guid projectId,
        string? formatFilter = null,
        bool sortByPeriodDescending = true,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var query = context.PerformanceRecords
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(formatFilter))
        {
            var format = formatFilter.Trim();
            query = query.Where(item => item.Format == format);
        }

        query = sortByPeriodDescending
            ? query.OrderByDescending(item => item.PeriodStart).ThenByDescending(item => item.PeriodLabel)
            : query.OrderBy(item => item.PeriodStart).ThenBy(item => item.PeriodLabel);

        var records = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<PerformanceRecord> GetPerformanceRecordAsync(
        Guid projectId,
        Guid performanceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.PerformanceRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == performanceId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Performance record '{performanceId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<PerformanceRecord> CreatePerformanceRecordAsync(
        Guid projectId,
        PerformanceRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureValid(ValidatePerformanceRecord(record));
        NormalizePerformance(record);

        await using var context = Open(projectId);
        var entity = ToRecord(record, projectId);
        if (entity.Id == Guid.Empty)
        {
            entity.Id = Guid.NewGuid();
        }

        entity.ProjectId = projectId;
        entity.LastEditedUtc = DateTimeOffset.UtcNow;
        context.PerformanceRecords.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(entity);
    }

    public async Task<PerformanceRecord> UpdatePerformanceRecordAsync(
        Guid projectId,
        PerformanceRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        EnsureValid(ValidatePerformanceRecord(record));
        NormalizePerformance(record);

        await using var context = Open(projectId);
        var entity = await context.PerformanceRecords
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == record.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Performance record '{record.Id}' was not found.");

        Apply(entity, record);
        entity.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(entity);
    }

    public async Task DeletePerformanceRecordAsync(
        Guid projectId,
        Guid performanceId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureConfirmed(confirmed);
        await using var context = Open(projectId);
        var entity = await context.PerformanceRecords
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == performanceId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Performance record '{performanceId}' was not found.");
        context.PerformanceRecords.Remove(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<Correction>> GetCorrectionsAsync(
        Guid projectId,
        CorrectionStatus? statusFilter = null,
        bool sortByReportedDescending = true,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var query = context.Corrections
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId);

        if (statusFilter is { } status)
        {
            query = query.Where(item => item.Status == status);
        }

        query = sortByReportedDescending
            ? query.OrderByDescending(item => item.ReportedDate).ThenBy(item => item.Error)
            : query.OrderBy(item => item.ReportedDate).ThenBy(item => item.Error);

        var records = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<Correction> GetCorrectionAsync(
        Guid projectId,
        Guid correctionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = Open(projectId);
        var record = await context.Corrections
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == correctionId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Correction '{correctionId}' was not found.");
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Correction> CreateCorrectionAsync(
        Guid projectId,
        Correction correction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(correction);
        EnsureValid(ValidateCorrection(correction));
        NormalizeCorrection(correction);

        await using var context = Open(projectId);
        var record = ToRecord(correction, projectId);
        if (record.Id == Guid.Empty)
        {
            record.Id = Guid.NewGuid();
        }

        record.ProjectId = projectId;
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        context.Corrections.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task<Correction> UpdateCorrectionAsync(
        Guid projectId,
        Correction correction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(correction);
        EnsureValid(ValidateCorrection(correction));
        NormalizeCorrection(correction);

        await using var context = Open(projectId);
        var record = await context.Corrections
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == correction.Id,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Correction '{correction.Id}' was not found.");

        Apply(record, correction);
        record.LastEditedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task DeleteCorrectionAsync(
        Guid projectId,
        Guid correctionId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureConfirmed(confirmed);
        await using var context = Open(projectId);
        var record = await context.Corrections
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.Id == correctionId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Correction '{correctionId}' was not found.");
        context.Corrections.Remove(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    public string? ResolveLocalLink(string projectRoot, string? relativeOrUrl, out string? missingMessage)
    {
        missingMessage = null;
        if (string.IsNullOrWhiteSpace(relativeOrUrl))
        {
            missingMessage = "No linked file path is set.";
            return null;
        }

        var trimmed = relativeOrUrl.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            missingMessage = "External URLs are stored for reference only and are not opened by this offline app.";
            return null;
        }

        if (!ProjectRelativePathRules.IsValid(trimmed))
        {
            missingMessage = $"Linked path is not a valid project-relative path: {trimmed}";
            return null;
        }

        var relative = ProjectRelativePathRules.Normalize(trimmed);
        var absolute = Path.GetFullPath(
            Path.Combine(projectRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(projectRoot);
        if (!absolute.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            missingMessage = $"Linked path escapes the project folder: {trimmed}";
            return null;
        }

        if (!File.Exists(absolute) && !Directory.Exists(absolute))
        {
            missingMessage = $"Linked file or folder was not found: {relative}";
            return null;
        }

        return absolute;
    }

    private ProjectDbContext Open(Guid projectId)
    {
        var project = _projectService.ActiveProject
            ?? throw new InvalidOperationException("No active project is open.");
        if (project.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return ProjectDbContextFactory.Create(
            Path.Combine(project.RootPath, ProjectPaths.DatabaseFileName));
    }

    private static void EnsureValid(PublishingValidationResult result)
    {
        if (!result.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors));
        }
    }

    private static void EnsureConfirmed(bool confirmed)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException("Delete was not confirmed.");
        }
    }

    private static void NormalizeSubmission(Submission submission)
    {
        submission.Name = submission.Name.Trim();
        submission.AgencyOrPublisher = submission.AgencyOrPublisher?.Trim() ?? string.Empty;
        submission.Website = NormalizeOptionalPath(submission.Website);
        submission.Fit = submission.Fit?.Trim() ?? string.Empty;
        submission.Requirements = submission.Requirements?.Trim() ?? string.Empty;
        submission.MaterialSent = submission.MaterialSent?.Trim() ?? string.Empty;
    }

    private static void NormalizeLaunchItem(LaunchItem item)
    {
        item.Asset = item.Asset.Trim();
        item.Phase = item.Phase?.Trim() ?? string.Empty;
        item.Channel = item.Channel?.Trim() ?? string.Empty;
        item.Audience = item.Audience?.Trim() ?? string.Empty;
        item.Owner = item.Owner?.Trim() ?? string.Empty;
        item.Link = NormalizeOptionalPath(item.Link);
        item.Result = item.Result?.Trim() ?? string.Empty;
    }

    private static void NormalizeContract(RightsAndContract contract)
    {
        contract.Party = contract.Party.Trim();
        contract.RightOrService = contract.RightOrService.Trim();
        contract.Territory = contract.Territory?.Trim() ?? string.Empty;
        contract.Format = contract.Format?.Trim() ?? string.Empty;
        contract.PaymentNotes = contract.PaymentNotes?.Trim() ?? string.Empty;
        contract.Restrictions = contract.Restrictions?.Trim() ?? string.Empty;
        contract.AgreementFile = NormalizeOptionalPath(contract.AgreementFile);
    }

    private static void NormalizeFormat(PublishingFormat format)
    {
        format.Name = format.Name.Trim();
        format.IsbnOrAsin = format.IsbnOrAsin?.Trim() ?? string.Empty;
        format.TrimOrFileSpec = format.TrimOrFileSpec?.Trim() ?? string.Empty;
        format.Distributor = format.Distributor?.Trim() ?? string.Empty;
        format.AssetLink = NormalizeOptionalPath(format.AssetLink);
        format.Notes = format.Notes?.Trim() ?? string.Empty;
    }

    private static void NormalizeMetadata(MetadataRecord record)
    {
        record.Title = record.Title.Trim();
        record.Subtitle = record.Subtitle?.Trim() ?? string.Empty;
        record.Series = record.Series?.Trim() ?? string.Empty;
        record.SeriesNumber = record.SeriesNumber?.Trim() ?? string.Empty;
        record.Author = record.Author?.Trim() ?? string.Empty;
        record.Description = record.Description?.Trim() ?? string.Empty;
        record.Categories = record.Categories?.Trim() ?? string.Empty;
        record.SearchTerms = record.SearchTerms?.Trim() ?? string.Empty;
        record.ReaderAge = record.ReaderAge?.Trim() ?? string.Empty;
        record.Language = record.Language?.Trim() ?? string.Empty;
        record.Edition = record.Edition?.Trim() ?? string.Empty;
        record.Publisher = record.Publisher?.Trim() ?? string.Empty;
        record.PricingNotes = record.PricingNotes?.Trim() ?? string.Empty;
        record.TerritoryRights = record.TerritoryRights?.Trim() ?? string.Empty;
        record.Isbn = record.Isbn?.Trim() ?? string.Empty;
        record.FormatName = record.FormatName?.Trim() ?? string.Empty;
    }

    private static void NormalizePerformance(PerformanceRecord record)
    {
        record.PeriodLabel = record.PeriodLabel.Trim();
        record.Format = record.Format?.Trim() ?? string.Empty;
        record.Availability = record.Availability?.Trim() ?? string.Empty;
        record.ReturnsOrIssues = record.ReturnsOrIssues?.Trim() ?? string.Empty;
        record.Notes = record.Notes?.Trim() ?? string.Empty;
    }

    private static void NormalizeCorrection(Correction correction)
    {
        correction.Error = correction.Error.Trim();
        correction.Location = correction.Location?.Trim() ?? string.Empty;
        correction.CorrectionText = correction.CorrectionText.Trim();
        correction.FormatsUpdated = correction.FormatsUpdated?.Trim() ?? string.Empty;
        correction.NewEdition = correction.NewEdition?.Trim() ?? string.Empty;
    }

    private static string NormalizeOptionalPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return ProjectRelativePathRules.Normalize(trimmed);
    }

    private static Submission ToDomain(SubmissionRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        Name = record.Name,
        AgencyOrPublisher = record.AgencyOrPublisher,
        Website = record.Website,
        Fit = record.Fit,
        Requirements = record.Requirements,
        MaterialSent = record.MaterialSent,
        SentDate = record.SentDate,
        FollowUpDate = record.FollowUpDate,
        Response = record.Response,
        Outcome = record.Outcome,
        RouteAffinity = record.RouteAffinity,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static LaunchItem ToDomain(LaunchItemRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        Date = record.Date,
        Phase = record.Phase,
        Channel = record.Channel,
        Asset = record.Asset,
        Audience = record.Audience,
        Owner = record.Owner,
        Link = record.Link,
        Status = record.Status,
        Result = record.Result,
        RouteAffinity = record.RouteAffinity,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static RightsAndContract ToDomain(RightsAndContractRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        Party = record.Party,
        RightOrService = record.RightOrService,
        Territory = record.Territory,
        Format = record.Format,
        StartDate = record.StartDate,
        EndOrReversionDate = record.EndOrReversionDate,
        Payment = record.Payment,
        PaymentNotes = record.PaymentNotes,
        Restrictions = record.Restrictions,
        AgreementFile = record.AgreementFile,
        Status = record.Status,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static SubmissionRecord ToRecord(Submission submission, Guid projectId) => new()
    {
        Id = submission.Id,
        ProjectId = projectId,
        Name = submission.Name,
        AgencyOrPublisher = submission.AgencyOrPublisher,
        Website = submission.Website,
        Fit = submission.Fit,
        Requirements = submission.Requirements,
        MaterialSent = submission.MaterialSent,
        SentDate = submission.SentDate,
        FollowUpDate = submission.FollowUpDate,
        Response = submission.Response,
        Outcome = submission.Outcome,
        RouteAffinity = submission.RouteAffinity,
        LastEditedUtc = submission.LastEditedUtc,
    };

    private static LaunchItemRecord ToRecord(LaunchItem item, Guid projectId) => new()
    {
        Id = item.Id,
        ProjectId = projectId,
        Date = item.Date,
        Phase = item.Phase,
        Channel = item.Channel,
        Asset = item.Asset,
        Audience = item.Audience,
        Owner = item.Owner,
        Link = item.Link,
        Status = item.Status,
        Result = item.Result,
        RouteAffinity = item.RouteAffinity,
        LastEditedUtc = item.LastEditedUtc,
    };

    private static RightsAndContractRecord ToRecord(RightsAndContract contract, Guid projectId) => new()
    {
        Id = contract.Id,
        ProjectId = projectId,
        Party = contract.Party,
        RightOrService = contract.RightOrService,
        Territory = contract.Territory,
        Format = contract.Format,
        StartDate = contract.StartDate,
        EndOrReversionDate = contract.EndOrReversionDate,
        Payment = contract.Payment,
        PaymentNotes = contract.PaymentNotes,
        Restrictions = contract.Restrictions,
        AgreementFile = contract.AgreementFile,
        Status = contract.Status,
        LastEditedUtc = contract.LastEditedUtc,
    };

    private static void Apply(SubmissionRecord record, Submission submission)
    {
        record.Name = submission.Name;
        record.AgencyOrPublisher = submission.AgencyOrPublisher;
        record.Website = submission.Website;
        record.Fit = submission.Fit;
        record.Requirements = submission.Requirements;
        record.MaterialSent = submission.MaterialSent;
        record.SentDate = submission.SentDate;
        record.FollowUpDate = submission.FollowUpDate;
        record.Response = submission.Response;
        record.Outcome = submission.Outcome;
        record.RouteAffinity = submission.RouteAffinity;
    }

    private static void Apply(LaunchItemRecord record, LaunchItem item)
    {
        record.Date = item.Date;
        record.Phase = item.Phase;
        record.Channel = item.Channel;
        record.Asset = item.Asset;
        record.Audience = item.Audience;
        record.Owner = item.Owner;
        record.Link = item.Link;
        record.Status = item.Status;
        record.Result = item.Result;
        record.RouteAffinity = item.RouteAffinity;
    }

    private static void Apply(RightsAndContractRecord record, RightsAndContract contract)
    {
        record.Party = contract.Party;
        record.RightOrService = contract.RightOrService;
        record.Territory = contract.Territory;
        record.Format = contract.Format;
        record.StartDate = contract.StartDate;
        record.EndOrReversionDate = contract.EndOrReversionDate;
        record.Payment = contract.Payment;
        record.PaymentNotes = contract.PaymentNotes;
        record.Restrictions = contract.Restrictions;
        record.AgreementFile = contract.AgreementFile;
        record.Status = contract.Status;
    }

    private static PublishingFormat ToDomain(PublishingFormatRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        Name = record.Name,
        FormatKind = record.FormatKind,
        IsbnOrAsin = record.IsbnOrAsin,
        TrimOrFileSpec = record.TrimOrFileSpec,
        Price = record.Price,
        Distributor = record.Distributor,
        PublicationStatus = record.PublicationStatus,
        AssetLink = record.AssetLink,
        Notes = record.Notes,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static MetadataRecord ToDomain(MetadataRecordEntity record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        Title = record.Title,
        Subtitle = record.Subtitle,
        Series = record.Series,
        SeriesNumber = record.SeriesNumber,
        Author = record.Author,
        Description = record.Description,
        Categories = record.Categories,
        SearchTerms = record.SearchTerms,
        ReaderAge = record.ReaderAge,
        Language = record.Language,
        PublicationDate = record.PublicationDate,
        Edition = record.Edition,
        Publisher = record.Publisher,
        PricingNotes = record.PricingNotes,
        TerritoryRights = record.TerritoryRights,
        Isbn = record.Isbn,
        FormatName = record.FormatName,
        PublicationStatus = record.PublicationStatus,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static PerformanceRecord ToDomain(PerformanceRecordEntity record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        PeriodLabel = record.PeriodLabel,
        PeriodStart = record.PeriodStart,
        PeriodEnd = record.PeriodEnd,
        Format = record.Format,
        Sales = record.Sales,
        ReadThrough = record.ReadThrough,
        MailingList = record.MailingList,
        Reviews = record.Reviews,
        AdCost = record.AdCost,
        Availability = record.Availability,
        ReturnsOrIssues = record.ReturnsOrIssues,
        Notes = record.Notes,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static Correction ToDomain(CorrectionRecord record) => new()
    {
        Id = record.Id,
        ProjectId = record.ProjectId,
        Error = record.Error,
        Location = record.Location,
        CorrectionText = record.CorrectionText,
        ReportedDate = record.ReportedDate,
        CorrectedDate = record.CorrectedDate,
        FormatsUpdated = record.FormatsUpdated,
        NewEdition = record.NewEdition,
        Status = record.Status,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static PublishingFormatRecord ToRecord(PublishingFormat format, Guid projectId) => new()
    {
        Id = format.Id,
        ProjectId = projectId,
        Name = format.Name,
        FormatKind = format.FormatKind,
        IsbnOrAsin = format.IsbnOrAsin,
        TrimOrFileSpec = format.TrimOrFileSpec,
        Price = format.Price,
        Distributor = format.Distributor,
        PublicationStatus = format.PublicationStatus,
        AssetLink = format.AssetLink,
        Notes = format.Notes,
        LastEditedUtc = format.LastEditedUtc,
    };

    private static MetadataRecordEntity ToRecord(MetadataRecord record, Guid projectId) => new()
    {
        Id = record.Id,
        ProjectId = projectId,
        Title = record.Title,
        Subtitle = record.Subtitle,
        Series = record.Series,
        SeriesNumber = record.SeriesNumber,
        Author = record.Author,
        Description = record.Description,
        Categories = record.Categories,
        SearchTerms = record.SearchTerms,
        ReaderAge = record.ReaderAge,
        Language = record.Language,
        PublicationDate = record.PublicationDate,
        Edition = record.Edition,
        Publisher = record.Publisher,
        PricingNotes = record.PricingNotes,
        TerritoryRights = record.TerritoryRights,
        Isbn = record.Isbn,
        FormatName = record.FormatName,
        PublicationStatus = record.PublicationStatus,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static PerformanceRecordEntity ToRecord(PerformanceRecord record, Guid projectId) => new()
    {
        Id = record.Id,
        ProjectId = projectId,
        PeriodLabel = record.PeriodLabel,
        PeriodStart = record.PeriodStart,
        PeriodEnd = record.PeriodEnd,
        Format = record.Format,
        Sales = record.Sales,
        ReadThrough = record.ReadThrough,
        MailingList = record.MailingList,
        Reviews = record.Reviews,
        AdCost = record.AdCost,
        Availability = record.Availability,
        ReturnsOrIssues = record.ReturnsOrIssues,
        Notes = record.Notes,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static CorrectionRecord ToRecord(Correction correction, Guid projectId) => new()
    {
        Id = correction.Id,
        ProjectId = projectId,
        Error = correction.Error,
        Location = correction.Location,
        CorrectionText = correction.CorrectionText,
        ReportedDate = correction.ReportedDate,
        CorrectedDate = correction.CorrectedDate,
        FormatsUpdated = correction.FormatsUpdated,
        NewEdition = correction.NewEdition,
        Status = correction.Status,
        LastEditedUtc = correction.LastEditedUtc,
    };

    private static void Apply(PublishingFormatRecord record, PublishingFormat format)
    {
        record.Name = format.Name;
        record.FormatKind = format.FormatKind;
        record.IsbnOrAsin = format.IsbnOrAsin;
        record.TrimOrFileSpec = format.TrimOrFileSpec;
        record.Price = format.Price;
        record.Distributor = format.Distributor;
        record.PublicationStatus = format.PublicationStatus;
        record.AssetLink = format.AssetLink;
        record.Notes = format.Notes;
    }

    private static void Apply(MetadataRecordEntity entity, MetadataRecord record)
    {
        entity.Title = record.Title;
        entity.Subtitle = record.Subtitle;
        entity.Series = record.Series;
        entity.SeriesNumber = record.SeriesNumber;
        entity.Author = record.Author;
        entity.Description = record.Description;
        entity.Categories = record.Categories;
        entity.SearchTerms = record.SearchTerms;
        entity.ReaderAge = record.ReaderAge;
        entity.Language = record.Language;
        entity.PublicationDate = record.PublicationDate;
        entity.Edition = record.Edition;
        entity.Publisher = record.Publisher;
        entity.PricingNotes = record.PricingNotes;
        entity.TerritoryRights = record.TerritoryRights;
        entity.Isbn = record.Isbn;
        entity.FormatName = record.FormatName;
        entity.PublicationStatus = record.PublicationStatus;
    }

    private static void Apply(PerformanceRecordEntity entity, PerformanceRecord record)
    {
        entity.PeriodLabel = record.PeriodLabel;
        entity.PeriodStart = record.PeriodStart;
        entity.PeriodEnd = record.PeriodEnd;
        entity.Format = record.Format;
        entity.Sales = record.Sales;
        entity.ReadThrough = record.ReadThrough;
        entity.MailingList = record.MailingList;
        entity.Reviews = record.Reviews;
        entity.AdCost = record.AdCost;
        entity.Availability = record.Availability;
        entity.ReturnsOrIssues = record.ReturnsOrIssues;
        entity.Notes = record.Notes;
    }

    private static void Apply(CorrectionRecord record, Correction correction)
    {
        record.Error = correction.Error;
        record.Location = correction.Location;
        record.CorrectionText = correction.CorrectionText;
        record.ReportedDate = correction.ReportedDate;
        record.CorrectedDate = correction.CorrectedDate;
        record.FormatsUpdated = correction.FormatsUpdated;
        record.NewEdition = correction.NewEdition;
        record.Status = correction.Status;
    }
}
