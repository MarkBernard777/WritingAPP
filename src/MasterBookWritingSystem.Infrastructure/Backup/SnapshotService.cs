using System.Security.Cryptography;
using System.Text;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Infrastructure.IO;
using Microsoft.Data.Sqlite;

namespace MasterBookWritingSystem.Infrastructure.Backup;

public sealed class SnapshotService : ISnapshotService
{
    private readonly IProjectService _projects;

    public SnapshotService(IProjectService projects)
    {
        _projects = projects;
    }

    public async Task<SnapshotInfo> CreateSnapshotAsync(
        Guid projectId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        var root = ProjectRootGuard.RequireActiveRoot(_projects, projectId);
        var project = _projects.ActiveProject!;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var snapshotName = $"snapshot-{stamp}";
        var snapshotRoot = Path.Combine(root, "13 Archive", "Snapshots", snapshotName);
        if (Directory.Exists(snapshotRoot))
        {
            throw new InvalidOperationException($"Snapshot already exists: {snapshotName}");
        }

        Directory.CreateDirectory(snapshotRoot);
        progress?.Report(new OperationProgress { Message = "Copying project files…", PercentComplete = 10 });

        var files = new List<SnapshotFileEntry>();
        try
        {
            await CopyProjectTreeAsync(root, snapshotRoot, files, cancellationToken, progress)
                .ConfigureAwait(false);

            progress?.Report(new OperationProgress { Message = "Backing up SQLite database…", PercentComplete = 70 });
            var dbRelative = ProjectPaths.DatabaseFileName;
            var dbDest = Path.Combine(snapshotRoot, dbRelative);
            await BackupSqliteAsync(
                    Path.Combine(root, ProjectPaths.DatabaseFileName),
                    dbDest,
                    cancellationToken)
                .ConfigureAwait(false);
            SqliteConnection.ClearAllPools();
            files.RemoveAll(item => item.RelativePath.Equals(dbRelative, StringComparison.OrdinalIgnoreCase));
            files.Add(new SnapshotFileEntry
            {
                RelativePath = dbRelative.Replace('\\', '/'),
                SizeBytes = new FileInfo(dbDest).Length,
                Sha256 = await ChecksumHelper.Sha256FileAsync(dbDest, cancellationToken).ConfigureAwait(false),
            });

            var validation = await _projects.ValidateAsync(root, cancellationToken).ConfigureAwait(false);
            var manifest = new SnapshotManifest
            {
                FormatVersion = SnapshotManifest.CurrentFormatVersion,
                ProjectId = project.Id,
                ProjectTitle = project.Title,
                CreatedUtc = DateTimeOffset.UtcNow,
                SchemaVersion = validation.SchemaVersion == 0
                    ? ProjectSchema.CurrentVersion
                    : validation.SchemaVersion,
                SnapshotName = snapshotName,
                Files = files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
            };

            var manifestPath = Path.Combine(snapshotRoot, "snapshot-manifest.json");
            await AtomicFileWriter
                .WriteAllTextAsync(manifestPath, ManifestSerializer.Serialize(manifest), cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new OperationProgress { Message = "Snapshot created", PercentComplete = 100 });
            return new SnapshotInfo
            {
                Name = snapshotName,
                DirectoryPath = snapshotRoot,
                Manifest = manifest,
                IsValid = true,
            };
        }
        catch
        {
            if (Directory.Exists(snapshotRoot))
            {
                try { Directory.Delete(snapshotRoot, recursive: true); } catch { /* ignore cleanup errors */ }
            }

            throw;
        }
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
        foreach (var directory in Directory.GetDirectories(snapshotsRoot).OrderByDescending(path => path))
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

            // Restore project.json if present in staging (may be listed in files).
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
                // Roll back
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

    private static async Task CopyProjectTreeAsync(
        string sourceRoot,
        string destRoot,
        List<SnapshotFileEntry> files,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            if (BackupPathRules.IsExcludedRelativePath(relative))
            {
                continue;
            }

            if (relative.Equals(ProjectPaths.DatabaseFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue; // handled via SQLite backup API
            }

            var dest = Path.Combine(destRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: false);
            files.Add(new SnapshotFileEntry
            {
                RelativePath = relative,
                SizeBytes = new FileInfo(dest).Length,
                Sha256 = await ChecksumHelper.Sha256FileAsync(dest, cancellationToken).ConfigureAwait(false),
            });
        }

        progress?.Report(new OperationProgress
        {
            Message = $"Copied {files.Count} files",
            PercentComplete = 55,
        });
    }

    private static async Task BackupSqliteAsync(string sourceDb, string destinationDb, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationDb)!);
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourceDb,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString());
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationDb,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
        source.BackupDatabase(destination);
        await destination.CloseAsync().ConfigureAwait(false);
        await source.CloseAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
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

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
