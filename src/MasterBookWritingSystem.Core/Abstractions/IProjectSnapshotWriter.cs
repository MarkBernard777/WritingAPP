using MasterBookWritingSystem.Core.Backup;

namespace MasterBookWritingSystem.Core.Abstractions;

public enum SafetySnapshotReason
{
    Migration = 0,
    ImportOverwrite = 1,
    Restore = 2,
    CompilationOrExport = 3,
}

public enum SafetySnapshotOutcome
{
    Created = 0,
    SkippedNoPendingWork = 1,
    SkippedNoChanges = 2,
}

public sealed class SafetySnapshotResult
{
    public required SafetySnapshotOutcome Outcome { get; init; }

    public required string Message { get; init; }

    public SnapshotInfo? Snapshot { get; init; }
}

public sealed class ProjectSnapshotWriteRequest
{
    public required string ProjectRootPath { get; init; }

    public required Guid ProjectId { get; init; }

    public required string ProjectTitle { get; init; }

    public required int SchemaVersion { get; init; }

    public SnapshotKind Kind { get; init; } = SnapshotKind.Manual;

    public required string NamePrefix { get; init; }
}

public interface IProjectSnapshotWriter
{
    Task<SnapshotInfo> WriteAsync(
        ProjectSnapshotWriteRequest request,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task EnforceRetentionAsync(
        string projectRootPath,
        CancellationToken cancellationToken = default);
}
