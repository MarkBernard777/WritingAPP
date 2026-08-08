using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Recovery;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IRecoveryCentreService
{
    Task<RecoveryCentreReport> DiagnoseAsync(
        string projectRootPath,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task<SnapshotRestorePreview> PreviewSnapshotAsync(
        string projectRootPath,
        string snapshotDirectoryPath,
        CancellationToken cancellationToken = default);

    Task<RecoveryActionResult> RestoreSnapshotAsync(
        string projectRootPath,
        string snapshotDirectoryPath,
        bool confirmed,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task<RecoveryActionResult> UnretireSnapshotAsync(
        string projectRootPath,
        string retiredSnapshotDirectoryPath,
        CancellationToken cancellationToken = default);

    Task<RecoveryJournalPreview?> PreviewJournalAsync(
        string projectRootPath,
        Guid entryId,
        CancellationToken cancellationToken = default);

    Task<RecoveryActionResult> RecoverJournalToCopyAsync(
        string projectRootPath,
        Guid entryId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task<RecoveryActionResult> RecoverJournalOverwriteAsync(
        string projectRootPath,
        Guid entryId,
        bool firstConfirmation,
        bool secondConfirmation,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);

    Task<RecoveryActionResult> RebuildMetadataAsync(
        string projectRootPath,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null);
}
