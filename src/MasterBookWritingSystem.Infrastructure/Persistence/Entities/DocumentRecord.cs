using MasterBookWritingSystem.Core.Domain.Documents;

namespace MasterBookWritingSystem.Infrastructure.Persistence.Entities;

public sealed class DocumentRecord
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public DocumentType DocumentType { get; set; }

    public required string Title { get; set; }

    public string Notes { get; set; } = string.Empty;

    public required string RelativeMarkdownPath { get; set; }

    public string? TemplatePath { get; set; }

    public int CompletionPercentage { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }

    public ICollection<DocumentFieldRecord> Fields { get; set; } = [];
}
