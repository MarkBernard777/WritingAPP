using MasterBookWritingSystem.Core.Documents;
using MasterBookWritingSystem.Core.Domain.Documents;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IDocumentTemplateCatalog
{
    int SchemaVersion { get; }

    Task<IReadOnlyList<DocumentTemplateDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<DocumentTemplateDefinition?> GetAsync(
        DocumentType documentType,
        CancellationToken cancellationToken = default);

    DocumentType? ResolveDocumentType(string? templatePath);
}

public interface IDocumentService
{
    Task EnsureDocumentsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkingDocument>> GetAllAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<WorkingDocument> GetAsync(
        Guid projectId,
        DocumentType documentType,
        CancellationToken cancellationToken = default);

    Task UpdateFieldAsync(
        Guid projectId,
        Guid documentId,
        string fieldKey,
        string value,
        CancellationToken cancellationToken = default);

    Task UpdateNotesAsync(
        Guid projectId,
        Guid documentId,
        string notes,
        CancellationToken cancellationToken = default);

    Task<DocumentValidationResult> ValidateAsync(
        Guid projectId,
        DocumentType documentType,
        CancellationToken cancellationToken = default);
}
