using System.Text.Json;
using System.Text.Json.Serialization;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Documents;
using MasterBookWritingSystem.Core.Domain.Documents;

namespace MasterBookWritingSystem.Infrastructure.Documents;

internal sealed class DocumentTemplateCatalogDto
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("documents")]
    public List<DocumentTemplateDto> Documents { get; set; } = [];
}

internal sealed class DocumentTemplateDto
{
    [JsonPropertyName("documentType")]
    public int DocumentType { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("templateFile")]
    public string TemplateFile { get; set; } = string.Empty;

    [JsonPropertyName("relativeFolder")]
    public string RelativeFolder { get; set; } = string.Empty;

    [JsonPropertyName("relativeMarkdownPath")]
    public string RelativeMarkdownPath { get; set; } = string.Empty;

    [JsonPropertyName("fields")]
    public List<DocumentFieldDto> Fields { get; set; } = [];
}

internal sealed class DocumentFieldDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public class FileDocumentTemplateCatalog : IDocumentTemplateCatalog
{
    private readonly string _schemaPath;
    private DocumentTemplateCatalogDto? _cached;

    public FileDocumentTemplateCatalog(string schemaPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaPath);
        _schemaPath = schemaPath;
    }

    public int SchemaVersion => Load().SchemaVersion;

    public Task<IReadOnlyList<DocumentTemplateDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var templates = Load().Documents.Select(ToDomain).ToList();
        return Task.FromResult<IReadOnlyList<DocumentTemplateDefinition>>(templates);
    }

    public Task<DocumentTemplateDefinition?> GetAsync(
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        var match = Load().Documents.FirstOrDefault(item => item.DocumentType == (int)documentType);
        return Task.FromResult(match is null ? null : ToDomain(match));
    }

    public DocumentType? ResolveDocumentType(string? templatePath)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return null;
        }

        var normalized = templatePath.Replace('\\', '/');
        var match = Load().Documents.FirstOrDefault(item =>
            normalized.EndsWith(item.TemplateFile.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, item.TemplateFile, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : (DocumentType)match.DocumentType;
    }

    private DocumentTemplateCatalogDto Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var json = File.ReadAllText(_schemaPath);
        _cached = JsonSerializer.Deserialize<DocumentTemplateCatalogDto>(json)
            ?? throw new InvalidOperationException($"Document template schema was empty: {_schemaPath}");
        return _cached;
    }

    private static DocumentTemplateDefinition ToDomain(DocumentTemplateDto dto) => new()
    {
        DocumentType = (DocumentType)dto.DocumentType,
        Title = dto.Title,
        TemplateFile = dto.TemplateFile,
        RelativeFolder = dto.RelativeFolder,
        RelativeMarkdownPath = dto.RelativeMarkdownPath.Replace('\\', '/'),
        Fields = dto.Fields.Select(field => new DocumentFieldDefinition
        {
            Key = field.Key,
            Label = field.Label,
            IsRequired = field.Required,
        }).ToList(),
    };
}

public sealed class SeedDocumentTemplateCatalog : FileDocumentTemplateCatalog
{
    public SeedDocumentTemplateCatalog()
        : base(ResolveDefaultPath())
    {
    }

    private static string ResolveDefaultPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "seed", "schemas", "document-templates.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate seed/schemas/document-templates.json.");
    }
}
