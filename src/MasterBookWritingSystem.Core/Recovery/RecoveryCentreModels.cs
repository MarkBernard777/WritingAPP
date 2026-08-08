using MasterBookWritingSystem.Core.Abstractions;

namespace MasterBookWritingSystem.Core.Recovery;

public sealed class RecoveryCentreReport
{
    public required string ProjectRootPath { get; init; }

    public Guid? ProjectId { get; init; }

    public string? ProjectTitle { get; init; }

    public required bool DatabaseExists { get; init; }

    public required bool MetadataExists { get; init; }

    public required ProjectIntegrityResult Integrity { get; init; }

    public required bool DatabaseReadable { get; init; }

    public IReadOnlyList<string> MissingChapterRelativePaths { get; init; } = [];

    public IReadOnlyList<string> DiagnosticNotes { get; init; } = [];

    public IReadOnlyList<RecoverySnapshotItem> ActiveSnapshots { get; init; } = [];

    public IReadOnlyList<RecoverySnapshotItem> RetiredSnapshots { get; init; } = [];

    public IReadOnlyList<RecoveryJournalInfo> JournalEntries { get; init; } = [];
}

public sealed class RecoverySnapshotItem
{
    public required string Name { get; init; }

    public required string DirectoryPath { get; init; }

    public required bool IsValid { get; init; }

    public required bool IsRetired { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required int FileCount { get; init; }

    public IReadOnlyList<string> ValidationErrors { get; init; } = [];

    public string DisplayName => IsValid
        ? $"{Name} · {FileCount} files · {CreatedUtc:u}"
        : $"{Name} · INVALID";
}

public sealed class RecoveryJournalPreview
{
    public required Guid EntryId { get; init; }

    public required RecoveryEntityType EntityType { get; init; }

    public required Guid EntityId { get; init; }

    public string? SecondaryKey { get; init; }

    public required DateTimeOffset UpdatedUtc { get; init; }

    public required int CharacterCount { get; init; }

    public required string DraftText { get; init; }

    public string Summary
        => $"{EntityType} · {CharacterCount} characters · updated {UpdatedUtc:u}";
}

public sealed class RecoveryActionResult
{
    public required bool Succeeded { get; init; }

    public required string Message { get; init; }

    public string? OutputPath { get; init; }
}
