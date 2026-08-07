namespace MasterBookWritingSystem.Core.Backup;

public sealed class SnapshotManifest
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public Guid ProjectId { get; set; }

    public string ProjectTitle { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public int SchemaVersion { get; set; }

    public string SnapshotName { get; set; } = string.Empty;

    public List<SnapshotFileEntry> Files { get; set; } = [];
}

public sealed class SnapshotFileEntry
{
    public string RelativePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class SnapshotInfo
{
    public required string Name { get; init; }

    public required string DirectoryPath { get; init; }

    public required SnapshotManifest Manifest { get; init; }

    public required bool IsValid { get; init; }

    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}

public sealed class SnapshotRestorePreview
{
    public required string SnapshotName { get; init; }

    public required Guid ProjectId { get; init; }

    public required string ProjectTitle { get; init; }

    public required int FileCount { get; init; }

    public required IReadOnlyList<string> IncludedRelativePaths { get; init; }

    public required IReadOnlyList<string> ValidationErrors { get; init; }

    public bool CanRestore => ValidationErrors.Count == 0;
}

public sealed class SnapshotRestoreResult
{
    public required bool Succeeded { get; init; }

    public required string Message { get; init; }

    public IReadOnlyList<string> Details { get; init; } = [];
}

public sealed class PortablePackageManifest
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public Guid ProjectId { get; set; }

    public string ProjectTitle { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public int SchemaVersion { get; set; }

    public List<SnapshotFileEntry> Files { get; set; } = [];
}

public sealed class ExportResult
{
    public required string RelativePath { get; init; }

    public required string AbsolutePath { get; init; }

    public required string Format { get; init; }
}

public sealed class OperationProgress
{
    public required string Message { get; init; }

    public double? PercentComplete { get; init; }
}
