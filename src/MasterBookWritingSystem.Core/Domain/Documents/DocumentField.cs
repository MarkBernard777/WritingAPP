namespace MasterBookWritingSystem.Core.Domain.Documents;

public sealed class DocumentField
{
    public required string Key { get; init; }

    public required string Label { get; set; }

    public string Value { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }
}
