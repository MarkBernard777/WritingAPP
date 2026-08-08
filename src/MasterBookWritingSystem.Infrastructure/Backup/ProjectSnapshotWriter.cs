using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Infrastructure.IO;
using Microsoft.Data.Sqlite;

namespace MasterBookWritingSystem.Infrastructure.Backup;

public sealed class ProjectSnapshotWriter : IProjectSnapshotWriter
{
    private readonly TimeProvider _timeProvider;
    private readonly IApplicationSettingsStore _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProjectSnapshotWriter(TimeProvider timeProvider, IApplicationSettingsStore settings)
    {
        _timeProvider = timeProvider;
        _settings = settings;
    }

    public async Task<SnapshotInfo> WriteAsync(
        ProjectSnapshotWriteRequest request,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NamePrefix);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(request.ProjectRootPath);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"Project root was not found: {root}");
            }

            var stamp = _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss-fff");
            var snapshotName = $"{request.NamePrefix}-{stamp}";
            var snapshotsRoot = Path.Combine(root, "13 Archive", "Snapshots");
            Directory.CreateDirectory(snapshotsRoot);
            var snapshotRoot = Path.Combine(snapshotsRoot, snapshotName);
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
                var dbSource = Path.Combine(root, dbRelative);
                if (File.Exists(dbSource))
                {
                    var dbDest = Path.Combine(snapshotRoot, dbRelative);
                    await BackupSqliteAsync(dbSource, dbDest, cancellationToken).ConfigureAwait(false);
                    SqliteConnection.ClearAllPools();
                    files.RemoveAll(item =>
                        item.RelativePath.Equals(dbRelative, StringComparison.OrdinalIgnoreCase));
                    files.Add(new SnapshotFileEntry
                    {
                        RelativePath = dbRelative.Replace('\\', '/'),
                        SizeBytes = new FileInfo(dbDest).Length,
                        Sha256 = await ChecksumHelper.Sha256FileAsync(dbDest, cancellationToken)
                            .ConfigureAwait(false),
                    });
                }

                var schemaVersion = request.SchemaVersion == 0
                    ? ProjectSchema.CurrentVersion
                    : request.SchemaVersion;
                var manifest = new SnapshotManifest
                {
                    FormatVersion = SnapshotManifest.CurrentFormatVersion,
                    ProjectId = request.ProjectId,
                    ProjectTitle = request.ProjectTitle,
                    CreatedUtc = _timeProvider.GetUtcNow(),
                    SchemaVersion = schemaVersion,
                    SnapshotName = snapshotName,
                    Kind = request.Kind,
                    Files = files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
                };

                var manifestPath = Path.Combine(snapshotRoot, "snapshot-manifest.json");
                await AtomicFileWriter
                    .WriteAllTextAsync(manifestPath, ManifestSerializer.Serialize(manifest), cancellationToken)
                    .ConfigureAwait(false);

                await EnforceRetentionUnlockedAsync(root, cancellationToken).ConfigureAwait(false);

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
                    try { Directory.Delete(snapshotRoot, recursive: true); } catch { /* ignore */ }
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnforceRetentionAsync(
        string projectRootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnforceRetentionUnlockedAsync(Path.GetFullPath(projectRootPath), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnforceRetentionUnlockedAsync(string root, CancellationToken cancellationToken)
    {
        var limit = _settings.GetSettings().SnapshotRetentionLimit;
        var snapshotsRoot = Path.Combine(root, "13 Archive", "Snapshots");
        if (!Directory.Exists(snapshotsRoot))
        {
            return;
        }

        var active = Directory.GetDirectories(snapshotsRoot)
            .Where(path => !SnapshotRetentionPolicy.IsRetiredDirectoryName(Path.GetFileName(path)))
            .Select(path => new
            {
                Path = path,
                CreatedUtc = ReadCreatedUtc(path),
            })
            .OrderByDescending(item => item.CreatedUtc)
            .ThenByDescending(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (active.Count <= limit)
        {
            return;
        }

        var retiredRoot = Path.Combine(snapshotsRoot, SnapshotRetentionPolicy.RetiredDirectoryName);
        Directory.CreateDirectory(retiredRoot);

        foreach (var excess in active.Skip(limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(excess.Path);
            var destination = Path.Combine(retiredRoot, name);
            if (Directory.Exists(destination))
            {
                destination = Path.Combine(
                    retiredRoot,
                    $"{name}-retired-{_timeProvider.GetUtcNow():yyyyMMddHHmmssfff}");
                if (Directory.Exists(destination))
                {
                    throw new InvalidOperationException(
                        $"Cannot retire snapshot '{name}' without overwriting an existing retired folder.");
                }
            }

            Directory.Move(excess.Path, destination);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static DateTimeOffset ReadCreatedUtc(string snapshotDirectory)
    {
        var manifestPath = Path.Combine(snapshotDirectory, "snapshot-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return Directory.GetCreationTimeUtc(snapshotDirectory);
        }

        try
        {
            var manifest = ManifestSerializer.DeserializeSnapshot(File.ReadAllText(manifestPath));
            return manifest.CreatedUtc;
        }
        catch
        {
            return Directory.GetCreationTimeUtc(snapshotDirectory);
        }
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

            if (relative.Equals(ProjectPaths.DatabaseFileName, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(ProjectPaths.DatabaseFileName + "-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
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
}
