using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using Microsoft.Data.Sqlite;

namespace MasterBookWritingSystem.Infrastructure.Backup;

public sealed class SnapshotService : ISnapshotService
{
    private readonly IProjectService _projects;
    private readonly IProjectSnapshotWriter _writer;
    private readonly TimeProvider _timeProvider;

    public SnapshotService(
        IProjectService projects,
        IProjectSnapshotWriter writer,
        TimeProvider timeProvider)
    {
        _projects = projects;
        _writer = writer;
        _timeProvider = timeProvider;
    }

    public Task<SnapshotInfo> CreateSnapshotAsync(
        Guid projectId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
        => CreateSnapshotCoreAsync(projectId, SnapshotKind.Manual, "snapshot", cancellationToken, progress);

    public async Task<AutomaticSnapshotResult> CreateAutomaticSnapshotIfNeededAsync(
        Guid projectId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        var snapshots = await ListSnapshotsAsync(projectId, cancellationToken).ConfigureAwait(false);
        var latestValid = snapshots
            .Where(snapshot => snapshot.IsValid)
            .OrderByDescending(snapshot => snapshot.Manifest.CreatedUtc)
            .FirstOrDefault();
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (latestValid is not null && GetLocalSnapshotDate(latestValid.Manifest.CreatedUtc) == today)
        {
            return new AutomaticSnapshotResult
            {
                Outcome = AutomaticSnapshotOutcome.AlreadyProtectedToday,
                Message = "A valid snapshot already protects today's project state.",
                Snapshot = latestValid,
            };
        }

        progress?.Report(new OperationProgress
        {
            Message = "Checking whether the project changed since its last snapshot...",
            PercentComplete = 5,
        });
        var candidate = await CreateSnapshotCoreAsync(
                projectId,
                SnapshotKind.Automatic,
                "auto",
                cancellationToken,
                progress)
            .ConfigureAwait(false);

        if (latestValid is not null
            && HasSameRestorableContent(candidate.Manifest, latestValid.Manifest))
        {
            if (TryDelete(candidate.DirectoryPath))
            {
                progress?.Report(new OperationProgress
                {
                    Message = "No project changes found; automatic snapshot was not needed.",
                    PercentComplete = 100,
                });
                return new AutomaticSnapshotResult
                {
                    Outcome = AutomaticSnapshotOutcome.NoChanges,
                    Message = "No project changes were found since the last valid snapshot.",
                    Snapshot = latestValid,
                };
            }

            return new AutomaticSnapshotResult
            {
                Outcome = AutomaticSnapshotOutcome.Created,
                Message = $"Automatic snapshot retained because temporary cleanup failed: {candidate.Name}.",
                Snapshot = candidate,
            };
        }

        return new AutomaticSnapshotResult
        {
            Outcome = AutomaticSnapshotOutcome.Created,
            Message = $"Automatic snapshot created: {candidate.Name}.",
            Snapshot = candidate,
        };
    }

    public async Task<SafetySnapshotResult> CreateSafetySnapshotAsync(
        Guid projectId,
        SafetySnapshotReason reason,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        if (reason == SafetySnapshotReason.CompilationOrExport)
        {
            return await CreateSafetySnapshotIfContentChangedAsync(projectId, cancellationToken, progress)
                .ConfigureAwait(false);
        }

        var prefix = reason switch
        {
            SafetySnapshotReason.Migration => "safety-migration",
            SafetySnapshotReason.ImportOverwrite => "safety-import",
            SafetySnapshotReason.Restore => "safety-restore",
            _ => "safety",
        };

        var snapshot = await CreateSnapshotCoreAsync(
                projectId,
                SnapshotKind.Safety,
                prefix,
                cancellationToken,
                progress)
            .ConfigureAwait(false);

        return new SafetySnapshotResult
        {
            Outcome = SafetySnapshotOutcome.Created,
            Message = $"Safety snapshot created: {snapshot.Name}.",
            Snapshot = snapshot,
        };
    }

    public async Task<IReadOnlyList<SnapshotInfo>> ListSnapshotsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var root = ProjectRootGuard.RequireActiveRoot(_projects, projectId);
        var snapshotsRoot = Path.Combine(root, "13 Archive", "Snapshots");
        if (!Directory.Exists(snapshotsRoot))
        {
            return [];
        }

        var results = new List<SnapshotInfo>();
        foreach (var directory in Directory.GetDirectories(snapshotsRoot)
                     .Where(path => !SnapshotRetentionPolicy.IsRetiredDirectoryName(Path.GetFileName(path)))
                     .OrderByDescending(path => path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await InspectSnapshotAsync(directory, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public async Task<SnapshotRestorePreview> PreviewRestoreAsync(
        Guid projectId,
        string snapshotDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        _ = ProjectRootGuard.RequireActiveRoot(_projects, projectId);
        var info = await InspectSnapshotAsync(snapshotDirectoryPath, cancellationToken).ConfigureAwait(false);
        return new SnapshotRestorePreview
        {
            SnapshotName = info.Name,
            ProjectId = info.Manifest.ProjectId,
            ProjectTitle = info.Manifest.ProjectTitle,
            FileCount = info.Manifest.Files.Count,
            IncludedRelativePaths = info.Manifest.Files.Select(item => item.RelativePath).ToList(),
            ValidationErrors = info.ValidationErrors,
        };
    }

    public async Task<SnapshotRestoreResult> RestoreSnapshotAsync(
        Guid projectId,
        string snapshotDirectoryPath,
        bool confirmed,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        if (!confirmed)
        {
            return new SnapshotRestoreResult
            {
                Succeeded = false,
                Message = "Restore cancelled: explicit confirmation is required.",
            };
        }

        var root = ProjectRootGuard.RequireActiveRoot(_projects, projectId);
        var preview = await PreviewRestoreAsync(projectId, snapshotDirectoryPath, cancellationToken)
            .ConfigureAwait(false);
        if (!preview.CanRestore)
        {
            return new SnapshotRestoreResult
            {
                Succeeded = false,
                Message = "Snapshot validation failed.",
                Details = preview.ValidationErrors,
            };
        }

        progress?.Report(new OperationProgress
        {
            Message = "Creating safety snapshot before restore…",
            PercentComplete = 5,
        });
        await CreateSafetySnapshotAsync(
                projectId,
                SafetySnapshotReason.Restore,
                cancellationToken,
                progress)
            .ConfigureAwait(false);

        var staging = Path.Combine(Path.GetTempPath(), "mbws-restore-" + Guid.NewGuid().ToString("N"));
        var backupOfCurrent = Path.Combine(Path.GetTempPath(), "mbws-current-" + Guid.NewGuid().ToString("N"));
        try
        {
            progress?.Report(new OperationProgress { Message = "Staging snapshot…", PercentComplete = 15 });
            Directory.CreateDirectory(staging);
            await CopyDirectoryAsync(snapshotDirectoryPath, staging, cancellationToken).ConfigureAwait(false);
            var stagedManifestPath = Path.Combine(staging, "snapshot-manifest.json");
            if (!File.Exists(stagedManifestPath))
            {
                throw new InvalidOperationException("Staged snapshot is missing its manifest.");
            }

            var stagedInfo = await InspectSnapshotAsync(staging, cancellationToken).ConfigureAwait(false);
            if (!stagedInfo.IsValid)
            {
                return new SnapshotRestoreResult
                {
                    Succeeded = false,
                    Message = "Staged snapshot failed checksum validation.",
                    Details = stagedInfo.ValidationErrors,
                };
            }

            progress?.Report(new OperationProgress { Message = "Protecting current project…", PercentComplete = 40 });
            await _projects.CloseAsync(cancellationToken).ConfigureAwait(false);
            SqliteConnection.ClearAllPools();
            Directory.CreateDirectory(backupOfCurrent);
            await CopyRestorableTreeAsync(root, backupOfCurrent, cancellationToken).ConfigureAwait(false);

            progress?.Report(new OperationProgress { Message = "Applying restored files…", PercentComplete = 65 });
            ClearRestorableContent(root);
            foreach (var entry in stagedInfo.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.Combine(staging, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var dest = Path.Combine(root, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(source, dest, overwrite: true);
            }

            var stagedMetadata = Path.Combine(staging, ProjectPaths.MetadataFileName);
            if (File.Exists(stagedMetadata))
            {
                File.Copy(stagedMetadata, Path.Combine(root, ProjectPaths.MetadataFileName), overwrite: true);
            }

            progress?.Report(new OperationProgress { Message = "Validating restored database…", PercentComplete = 85 });
            var opened = await _projects.OpenAsync(root, cancellationToken).ConfigureAwait(false);
            var validation = await _projects.ValidateAsync(root, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid || opened.Id != projectId && opened.Id != stagedInfo.Manifest.ProjectId)
            {
                await _projects.CloseAsync(cancellationToken).ConfigureAwait(false);
                SqliteConnection.ClearAllPools();
                ClearRestorableContent(root);
                await CopyDirectoryAsync(backupOfCurrent, root, cancellationToken).ConfigureAwait(false);
                await _projects.OpenAsync(root, cancellationToken).ConfigureAwait(false);
                return new SnapshotRestoreResult
                {
                    Succeeded = false,
                    Message = "Restored project failed validation; previous project files were reinstated.",
                    Details = validation.Errors,
                };
            }

            progress?.Report(new OperationProgress { Message = "Restore complete", PercentComplete = 100 });
            return new SnapshotRestoreResult
            {
                Succeeded = true,
                Message = $"Restored snapshot '{preview.SnapshotName}' ({preview.FileCount} files).",
                Details = preview.IncludedRelativePaths,
            };
        }
        catch (Exception ex)
        {
            try
            {
                await _projects.CloseAsync(cancellationToken).ConfigureAwait(false);
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(backupOfCurrent))
                {
                    ClearRestorableContent(root);
                    await CopyDirectoryAsync(backupOfCurrent, root, cancellationToken).ConfigureAwait(false);
                    await _projects.OpenAsync(root, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                // Preserve original exception.
            }

            return new SnapshotRestoreResult
            {
                Succeeded = false,
                Message = $"Restore failed: {ex.Message}",
            };
        }
        finally
        {
            TryDelete(staging);
            TryDelete(backupOfCurrent);
        }
    }

    private async Task<SafetySnapshotResult> CreateSafetySnapshotIfContentChangedAsync(
        Guid projectId,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        var snapshots = await ListSnapshotsAsync(projectId, cancellationToken).ConfigureAwait(false);
        var latestValid = snapshots
            .Where(snapshot => snapshot.IsValid)
            .OrderByDescending(snapshot => snapshot.Manifest.CreatedUtc)
            .FirstOrDefault();

        var candidate = await CreateSnapshotCoreAsync(
                projectId,
                SnapshotKind.Safety,
                "safety-export",
                cancellationToken,
                progress)
            .ConfigureAwait(false);

        if (latestValid is not null
            && HasSameRestorableContent(candidate.Manifest, latestValid.Manifest))
        {
            if (TryDelete(candidate.DirectoryPath))
            {
                return new SafetySnapshotResult
                {
                    Outcome = SafetySnapshotOutcome.SkippedNoChanges,
                    Message = "No project changes since the latest valid snapshot; safety snapshot was not needed.",
                    Snapshot = latestValid,
                };
            }
        }

        return new SafetySnapshotResult
        {
            Outcome = SafetySnapshotOutcome.Created,
            Message = $"Safety snapshot created: {candidate.Name}.",
            Snapshot = candidate,
        };
    }

    private async Task<SnapshotInfo> CreateSnapshotCoreAsync(
        Guid projectId,
        SnapshotKind kind,
        string namePrefix,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        var root = ProjectRootGuard.RequireActiveRoot(_projects, projectId);
        var project = _projects.ActiveProject!;
        var validation = await _projects.ValidateAsync(root, cancellationToken).ConfigureAwait(false);
        return await _writer.WriteAsync(
                new ProjectSnapshotWriteRequest
                {
                    ProjectRootPath = root,
                    ProjectId = project.Id,
                    ProjectTitle = project.Title,
                    SchemaVersion = validation.SchemaVersion == 0
                        ? ProjectSchema.CurrentVersion
                        : validation.SchemaVersion,
                    Kind = kind,
                    NamePrefix = namePrefix,
                },
                cancellationToken,
                progress)
            .ConfigureAwait(false);
    }

    private async Task<SnapshotInfo> InspectSnapshotAsync(string directory, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var manifestPath = Path.Combine(directory, "snapshot-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new SnapshotInfo
            {
                Name = name,
                DirectoryPath = directory,
                Manifest = new SnapshotManifest { SnapshotName = name },
                IsValid = false,
                ValidationErrors = ["Missing snapshot-manifest.json."],
            };
        }

        SnapshotManifest manifest;
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            manifest = ManifestSerializer.DeserializeSnapshot(json);
        }
        catch (Exception ex)
        {
            return new SnapshotInfo
            {
                Name = name,
                DirectoryPath = directory,
                Manifest = new SnapshotManifest { SnapshotName = name },
                IsValid = false,
                ValidationErrors = [$"Invalid manifest: {ex.Message}"],
            };
        }

        if (manifest.FormatVersion != SnapshotManifest.CurrentFormatVersion)
        {
            errors.Add($"Unsupported snapshot format version {manifest.FormatVersion}.");
        }

        if (manifest.Files.Count == 0)
        {
            errors.Add("Manifest contains no files.");
        }

        foreach (var entry in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.RelativePath)
                || entry.RelativePath.Contains("..", StringComparison.Ordinal)
                || Path.IsPathRooted(entry.RelativePath))
            {
                errors.Add($"Unsafe relative path in manifest: {entry.RelativePath}");
                continue;
            }

            var absolute = Path.Combine(directory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolute))
            {
                errors.Add($"Missing file: {entry.RelativePath}");
                continue;
            }

            var hash = await ChecksumHelper.Sha256FileAsync(absolute, cancellationToken).ConfigureAwait(false);
            if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Checksum mismatch: {entry.RelativePath}");
            }
        }

        return new SnapshotInfo
        {
            Name = string.IsNullOrWhiteSpace(manifest.SnapshotName) ? name : manifest.SnapshotName,
            DirectoryPath = directory,
            Manifest = manifest,
            IsValid = errors.Count == 0,
            ValidationErrors = errors,
        };
    }

    private DateOnly GetLocalSnapshotDate(DateTimeOffset createdUtc)
    {
        var local = TimeZoneInfo.ConvertTime(createdUtc, _timeProvider.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static bool HasSameRestorableContent(SnapshotManifest first, SnapshotManifest second)
    {
        static Dictionary<string, SnapshotFileEntry> ComparableFiles(SnapshotManifest manifest)
            => manifest.Files
                .Where(file => !file.RelativePath.Equals(
                    ProjectPaths.MetadataFileName,
                    StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    file => file.RelativePath.Replace('\\', '/'),
                    StringComparer.OrdinalIgnoreCase);

        var firstFiles = ComparableFiles(first);
        var secondFiles = ComparableFiles(second);
        if (firstFiles.Count != secondFiles.Count)
        {
            return false;
        }

        return firstFiles.All(pair =>
            secondFiles.TryGetValue(pair.Key, out var other)
            && pair.Value.SizeBytes == other.SizeBytes
            && pair.Value.Sha256.Equals(other.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task CopyRestorableTreeAsync(string sourceRoot, string destRoot, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            if (BackupPathRules.IsExcludedRelativePath(relative))
            {
                continue;
            }

            var dest = Path.Combine(destRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async Task CopyDirectoryAsync(string sourceRoot, string destRoot, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, file);
            var dest = Path.Combine(destRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static void ClearRestorableContent(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList())
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (BackupPathRules.IsExcludedRelativePath(relative))
            {
                continue;
            }

            File.Delete(file);
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
