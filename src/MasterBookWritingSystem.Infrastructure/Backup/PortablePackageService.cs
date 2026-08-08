using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Infrastructure.IO;
using MasterBookWritingSystem.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace MasterBookWritingSystem.Infrastructure.Backup;

public sealed class PortablePackageService : IPortablePackageService
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    private readonly IProjectService _projects;
    private readonly IProjectSnapshotWriter _snapshotWriter;

    public PortablePackageService(IProjectService projects, IProjectSnapshotWriter snapshotWriter)
    {
        _projects = projects;
        _snapshotWriter = snapshotWriter;
    }

    public async Task<ExportResult> ExportZipAsync(
        Guid projectId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        var root = ProjectRootGuard.RequireActiveRoot(_projects, projectId);
        var project = _projects.ActiveProject!;
        var fileName = ExportFileNames.Timestamped("portable-project", ".zip");
        var zipAbsolute = ProjectRootGuard.EnsureUniqueExportPath(root, fileName);
        var staging = Path.Combine(Path.GetTempPath(), "mbws-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            progress?.Report(new OperationProgress { Message = "Collecting portable package files…", PercentComplete = 10 });
            var files = new List<SnapshotFileEntry>();
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (BackupPathRules.IsExcludedRelativePath(relative))
                {
                    continue;
                }

                if (relative.Equals(ProjectPaths.DatabaseFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dest = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: false);
                files.Add(new SnapshotFileEntry
                {
                    RelativePath = relative,
                    SizeBytes = new FileInfo(dest).Length,
                    Sha256 = await ChecksumHelper.Sha256FileAsync(dest, cancellationToken).ConfigureAwait(false),
                });
            }

            var dbDest = Path.Combine(staging, ProjectPaths.DatabaseFileName);
            await BackupSqliteAsync(Path.Combine(root, ProjectPaths.DatabaseFileName), dbDest, cancellationToken)
                .ConfigureAwait(false);
            SqliteConnection.ClearAllPools();
            files.Add(new SnapshotFileEntry
            {
                RelativePath = ProjectPaths.DatabaseFileName,
                SizeBytes = new FileInfo(dbDest).Length,
                Sha256 = await ChecksumHelper.Sha256FileAsync(dbDest, cancellationToken).ConfigureAwait(false),
            });

            var validation = await _projects.ValidateAsync(root, cancellationToken).ConfigureAwait(false);
            var manifest = new PortablePackageManifest
            {
                FormatVersion = PortablePackageManifest.CurrentFormatVersion,
                ProjectId = project.Id,
                ProjectTitle = project.Title,
                CreatedUtc = DateTimeOffset.UtcNow,
                SchemaVersion = validation.SchemaVersion == 0
                    ? ProjectSchema.CurrentVersion
                    : validation.SchemaVersion,
                Files = files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
            };
            await AtomicFileWriter
                .WriteAllTextAsync(
                    Path.Combine(staging, "package-manifest.json"),
                    ManifestSerializer.Serialize(manifest),
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new OperationProgress { Message = "Creating ZIP…", PercentComplete = 70 });
            ZipFile.CreateFromDirectory(staging, zipAbsolute, CompressionLevel.Optimal, includeBaseDirectory: false);
            progress?.Report(new OperationProgress { Message = "Portable ZIP ready", PercentComplete = 100 });
            return new ExportResult
            {
                AbsolutePath = zipAbsolute,
                RelativePath = ProjectRootGuard.ToRelativeExport(root, zipAbsolute),
                Format = "ZIP",
            };
        }
        catch
        {
            if (File.Exists(zipAbsolute))
            {
                File.Delete(zipAbsolute);
            }

            throw;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    public async Task ImportZipAsync(
        string zipFilePath,
        string destinationProjectDirectory,
        bool overwriteExisting,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationProjectDirectory);
        if (!File.Exists(zipFilePath))
        {
            throw new FileNotFoundException("ZIP package was not found.", zipFilePath);
        }

        var destination = Path.GetFullPath(destinationProjectDirectory);
        var destinationExists = Directory.Exists(destination)
            && (File.Exists(Path.Combine(destination, ProjectPaths.DatabaseFileName))
                || File.Exists(Path.Combine(destination, ProjectPaths.MetadataFileName))
                || Directory.EnumerateFileSystemEntries(destination).Any());

        if (destinationExists && !overwriteExisting)
        {
            throw new InvalidOperationException(
                $"Destination already exists: {destination}. Confirm overwrite explicitly to replace it.");
        }

        var staging = Path.Combine(Path.GetTempPath(), "mbws-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            progress?.Report(new OperationProgress { Message = "Extracting package…", PercentComplete = 15 });
            ExtractZipSafely(zipFilePath, staging);

            var manifestPath = Path.Combine(staging, "package-manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("Package is missing package-manifest.json.");
            }

            var manifest = ManifestSerializer.DeserializePortable(
                await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
            if (manifest.FormatVersion != PortablePackageManifest.CurrentFormatVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported package format version {manifest.FormatVersion}.");
            }

            if (manifest.SchemaVersion > ProjectSchema.CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"Package schema version {manifest.SchemaVersion} is newer than this application ({ProjectSchema.CurrentVersion}).");
            }

            progress?.Report(new OperationProgress { Message = "Validating checksums…", PercentComplete = 40 });
            foreach (var entry in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateRelativePath(entry.RelativePath);
                var absolute = Path.Combine(staging, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(absolute))
                {
                    throw new InvalidOperationException($"Package missing file: {entry.RelativePath}");
                }

                var hash = await ChecksumHelper.Sha256FileAsync(absolute, cancellationToken).ConfigureAwait(false);
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Checksum mismatch: {entry.RelativePath}");
                }
            }

            progress?.Report(new OperationProgress { Message = "Writing project files…", PercentComplete = 70 });
            if (destinationExists)
            {
                await CreatePreImportSafetySnapshotAsync(destination, cancellationToken, progress)
                    .ConfigureAwait(false);
                ClearRestorableContent(destination);
            }
            else
            {
                Directory.CreateDirectory(destination);
            }

            foreach (var entry in manifest.Files)
            {
                var source = Path.Combine(staging, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var dest = Path.Combine(destination, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(source, dest, overwrite: true);
            }

            // Copy metadata if present but not listed (should be listed).
            var stagedMeta = Path.Combine(staging, ProjectPaths.MetadataFileName);
            if (File.Exists(stagedMeta))
            {
                File.Copy(stagedMeta, Path.Combine(destination, ProjectPaths.MetadataFileName), overwrite: true);
            }

            progress?.Report(new OperationProgress { Message = "Validating imported project…", PercentComplete = 90 });
            var validation = await _projects.ValidateAsync(destination, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(
                    "Imported project failed validation: " + string.Join(" ", validation.Errors));
            }

            progress?.Report(new OperationProgress { Message = "Import complete", PercentComplete = 100 });
        }
        catch
        {
            TryDelete(staging);
            if (!destinationExists && Directory.Exists(destination))
            {
                TryDelete(destination);
            }

            throw;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private async Task CreatePreImportSafetySnapshotAsync(
        string destination,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        progress?.Report(new OperationProgress
        {
            Message = "Creating safety snapshot before overwrite…",
            PercentComplete = 60,
        });

        var identity = await TryReadDestinationIdentityAsync(destination, cancellationToken)
            .ConfigureAwait(false);
        await _snapshotWriter.WriteAsync(
                new ProjectSnapshotWriteRequest
                {
                    ProjectRootPath = destination,
                    ProjectId = identity.Id,
                    ProjectTitle = identity.Title,
                    SchemaVersion = identity.SchemaVersion,
                    Kind = SnapshotKind.Safety,
                    NamePrefix = "safety-import",
                },
                cancellationToken,
                progress)
            .ConfigureAwait(false);
    }

    private static async Task<(Guid Id, string Title, int SchemaVersion)> TryReadDestinationIdentityAsync(
        string destination,
        CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(destination, ProjectPaths.MetadataFileName);
        if (File.Exists(metadataPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
                var document = JsonSerializer.Deserialize<ProjectMetadataDocument>(json, MetadataJsonOptions);
                if (document is not null && document.Id != Guid.Empty)
                {
                    return (document.Id, document.Title, document.SchemaVersion);
                }
            }
            catch
            {
                // Fall through.
            }
        }

        return (
            Guid.Empty,
            Path.GetFileName(destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            0);
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

    private static void ExtractZipSafely(string zipFilePath, string destinationDirectory)
    {
        var destinationFull = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipFilePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                continue; // directory entry
            }

            var relative = entry.FullName.Replace('\\', '/');
            ValidateRelativePath(relative);
            var target = Path.GetFullPath(Path.Combine(destinationDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destinationFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"ZIP entry escapes destination: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains("..", StringComparison.Ordinal)
            || relativePath.StartsWith('/')
            || relativePath.StartsWith('\\')
            || relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsafe archive path rejected: {relativePath}");
        }
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

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort
        }
    }
}
