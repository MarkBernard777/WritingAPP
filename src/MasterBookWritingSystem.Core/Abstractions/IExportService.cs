using MasterBookWritingSystem.Core.Backup;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IExportService
{
    Task<ExportResult> ExportManuscriptDocxAsync(
        Guid projectId,
        IReadOnlyList<Guid>? chapterIdsInOrder,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task<ExportResult> ExportProjectJsonAsync(
        Guid projectId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task<ExportResult> ExportStoryDataCsvAsync(
        Guid projectId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);
}

public interface ISnapshotService
{
    Task<SnapshotInfo> CreateSnapshotAsync(
        Guid projectId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task<IReadOnlyList<SnapshotInfo>> ListSnapshotsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<SnapshotRestorePreview> PreviewRestoreAsync(
        Guid projectId,
        string snapshotDirectoryPath,
        CancellationToken cancellationToken = default);

    Task<SnapshotRestoreResult> RestoreSnapshotAsync(
        Guid projectId,
        string snapshotDirectoryPath,
        bool confirmed,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);
}

public interface IPortablePackageService
{
    Task<ExportResult> ExportZipAsync(
        Guid projectId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task ImportZipAsync(
        string zipFilePath,
        string destinationProjectDirectory,
        bool overwriteExisting,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);
}
