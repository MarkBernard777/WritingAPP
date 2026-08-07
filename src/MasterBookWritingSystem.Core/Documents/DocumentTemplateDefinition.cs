using MasterBookWritingSystem.Core.Domain.Documents;

namespace MasterBookWritingSystem.Core.Documents;

public sealed class DocumentFieldDefinition
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public bool IsRequired { get; init; }
}

public sealed class DocumentTemplateDefinition
{
    public required DocumentType DocumentType { get; init; }

    public required string Title { get; init; }

    public required string TemplateFile { get; init; }

    public required string RelativeFolder { get; init; }

    public required string RelativeMarkdownPath { get; init; }

    public IReadOnlyList<DocumentFieldDefinition> Fields { get; init; } = [];
}

public sealed class DocumentValidationResult
{
    public required bool IsComplete { get; init; }

    public int CompletionPercentage { get; init; }

    public IReadOnlyList<string> MissingRequiredFieldKeys { get; init; } = [];
}
