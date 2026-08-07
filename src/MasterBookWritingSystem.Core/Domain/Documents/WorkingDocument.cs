namespace MasterBookWritingSystem.Core.Domain.Documents;

public sealed class WorkingDocument
{
    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required DocumentType DocumentType { get; init; }

    public required string Title { get; set; }

    public string Notes { get; set; } = string.Empty;

    public required string RelativeMarkdownPath { get; set; }

    public string? TemplatePath { get; set; }

    public int CompletionPercentage { get; set; }

    public DateTimeOffset? LastEditedUtc { get; set; }

    public IList<DocumentField> Fields { get; init; } = [];
}
