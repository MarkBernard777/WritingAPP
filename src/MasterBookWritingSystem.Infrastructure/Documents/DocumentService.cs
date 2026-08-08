using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Documents;
using MasterBookWritingSystem.Core.Domain.Documents;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Core.Workflow;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Documents;

public sealed class DocumentService : IDocumentService
{
    private readonly IProjectService _projectService;
    private readonly IDocumentTemplateCatalog _catalog;
    private readonly IRecoveryJournalService _journal;

    public DocumentService(
        IProjectService projectService,
        IDocumentTemplateCatalog catalog,
        IRecoveryJournalService journal)
    {
        _projectService = projectService;
        _catalog = catalog;
        _journal = journal;
    }

    public async Task EnsureDocumentsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var templates = await _catalog.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            await DocumentSeeder
                .SeedAsync(context, projectId, rootPath, templates, DocumentSeeder.FindSeedRoot(), cancellationToken)
                .ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<WorkingDocument>> GetAllAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureDocumentsAsync(projectId, cancellationToken).ConfigureAwait(false);

        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using var context = ProjectDbContextFactory.Create(databasePath);
        var records = await context.Documents
            .AsNoTracking()
            .Include(document => document.Fields)
            .Where(document => document.ProjectId == projectId)
            .OrderBy(document => document.DocumentType)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<WorkingDocument> GetAsync(
        Guid projectId,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        await EnsureDocumentsAsync(projectId, cancellationToken).ConfigureAwait(false);

        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using var context = ProjectDbContextFactory.Create(databasePath);
        var record = await context.Documents
            .AsNoTracking()
            .Include(document => document.Fields)
            .FirstOrDefaultAsync(
                document => document.ProjectId == projectId && document.DocumentType == documentType,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Document '{documentType}' was not found.");

        SqliteConnection.ClearAllPools();
        return ToDomain(record);
    }

    public async Task UpdateFieldAsync(
        Guid projectId,
        Guid documentId,
        string fieldKey,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);
        var draft = value ?? string.Empty;
        var entryId = RecoveryJournalIds.Create(
            projectId,
            RecoveryEntityType.DocumentField,
            documentId,
            fieldKey);
        await _journal.UpsertAsync(
                new RecoveryJournalEntry
                {
                    EntryId = entryId,
                    ProjectId = projectId,
                    EntityType = RecoveryEntityType.DocumentField,
                    EntityId = documentId,
                    SecondaryKey = fieldKey,
                    DraftText = draft,
                },
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await using (var context = ProjectDbContextFactory.Create(databasePath))
            {
                await using var transaction = await context.Database
                    .BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);

                var document = await context.Documents
                    .Include(item => item.Fields)
                    .FirstOrDefaultAsync(
                        item => item.ProjectId == projectId && item.Id == documentId,
                        cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Document '{documentId}' was not found.");

                var field = document.Fields.FirstOrDefault(item => item.Key == fieldKey)
                    ?? throw new InvalidOperationException($"Field '{fieldKey}' was not found.");

                field.Value = draft;
                document.LastEditedUtc = DateTimeOffset.UtcNow;
                document.CompletionPercentage = PhaseGateRules.CalculateCompletionPercentage(
                    document.Fields.Select(item => new DocumentField
                    {
                        Key = item.Key,
                        Label = item.Label,
                        Value = item.Value,
                        IsRequired = item.IsRequired,
                        SortOrder = item.SortOrder,
                    }));

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            SqliteConnection.ClearAllPools();
            await _journal.ClearAsync(projectId, entryId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            throw;
        }
    }

    public async Task UpdateNotesAsync(
        Guid projectId,
        Guid documentId,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);
        var draft = notes ?? string.Empty;
        var entryId = RecoveryJournalIds.Create(
            projectId,
            RecoveryEntityType.DocumentNotes,
            documentId);
        await _journal.UpsertAsync(
                new RecoveryJournalEntry
                {
                    EntryId = entryId,
                    ProjectId = projectId,
                    EntityType = RecoveryEntityType.DocumentNotes,
                    EntityId = documentId,
                    DraftText = draft,
                },
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await using (var context = ProjectDbContextFactory.Create(databasePath))
            {
                var document = await context.Documents
                    .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.Id == documentId, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Document '{documentId}' was not found.");

                document.Notes = draft;
                document.LastEditedUtc = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            SqliteConnection.ClearAllPools();
            await _journal.ClearAsync(projectId, entryId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            throw;
        }
    }

    public async Task<DocumentValidationResult> ValidateAsync(
        Guid projectId,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        var document = await GetAsync(projectId, documentType, cancellationToken).ConfigureAwait(false);
        var missing = document.Fields
            .Where(field => field.IsRequired && string.IsNullOrWhiteSpace(field.Value))
            .Select(field => field.Key)
            .ToList();

        return new DocumentValidationResult
        {
            IsComplete = missing.Count == 0,
            CompletionPercentage = document.CompletionPercentage,
            MissingRequiredFieldKeys = missing,
        };
    }

    private string RequireActiveRoot(Guid projectId)
    {
        var active = _projectService.ActiveProject
            ?? throw new InvalidOperationException("Open a project before using document services.");
        if (active.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return active.RootPath;
    }

    private static WorkingDocument ToDomain(DocumentRecord record)
    {
        var fields = record.Fields
            .OrderBy(field => field.SortOrder)
            .Select(field => new DocumentField
            {
                Key = field.Key,
                Label = field.Label,
                Value = field.Value,
                IsRequired = field.IsRequired,
                SortOrder = field.SortOrder,
            })
            .ToList();

        return new WorkingDocument
        {
            Id = record.Id,
            ProjectId = record.ProjectId,
            DocumentType = record.DocumentType,
            Title = record.Title,
            Notes = record.Notes,
            RelativeMarkdownPath = record.RelativeMarkdownPath,
            TemplatePath = record.TemplatePath,
            CompletionPercentage = record.CompletionPercentage,
            LastEditedUtc = record.LastEditedUtc,
            Fields = fields,
        };
    }
}
