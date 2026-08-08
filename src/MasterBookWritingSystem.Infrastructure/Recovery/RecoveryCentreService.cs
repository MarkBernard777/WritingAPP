using System.Text.Json;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.IO;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Recovery;

public sealed class RecoveryCentreService : IRecoveryCentreService
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions JournalJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IProjectIntegrityService _integrity;
    private readonly IProjectService _projects;
    private readonly IProjectSnapshotWriter _snapshotWriter;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecoveryCentreService(
        IProjectIntegrityService integrity,
        IProjectService projects,
        IProjectSnapshotWriter snapshotWriter,
        TimeProvider timeProvider)
    {
        _integrity = integrity;
        _projects = projects;
        _snapshotWriter = snapshotWriter;
        _timeProvider = timeProvider;
    }

    public async Task<RecoveryCentreReport> DiagnoseAsync(
        string projectRootPath,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        var root = Path.GetFullPath(projectRootPath);
        progress?.Report(new OperationProgress { Message = "Running recovery diagnostics…", PercentComplete = 5 });

        var databasePath = Path.Combine(root, ProjectPaths.DatabaseFileName);
        var metadataPath = Path.Combine(root, ProjectPaths.MetadataFileName);
        var databaseExists = File.Exists(databasePath);
        var metadataExists = File.Exists(metadataPath);
        var notes = new List<string>();
        Guid? projectId = null;
        string? projectTitle = null;
        var missingChapters = new List<string>();
        var databaseReadable = false;

        var integrity = databaseExists
            ? await _integrity.CheckAsync(root, cancellationToken).ConfigureAwait(false)
            : new ProjectIntegrityResult
            {
                IsHealthy = false,
                Summary = $"Missing {ProjectPaths.DatabaseFileName}.",
                CheckedUtc = _timeProvider.GetUtcNow(),
            };

        if (!databaseExists)
        {
            notes.Add($"Missing {ProjectPaths.DatabaseFileName}.");
        }

        if (!metadataExists)
        {
            notes.Add($"Missing {ProjectPaths.MetadataFileName}.");
        }

        if (databaseExists && integrity.IsHealthy)
        {
            progress?.Report(new OperationProgress { Message = "Inspecting database structure…", PercentComplete = 25 });
            try
            {
                await using var context = ProjectDbContextFactory.Create(databasePath);
                if (await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                    && await TableExistsAsync(context, "Projects", cancellationToken).ConfigureAwait(false))
                {
                    databaseReadable = true;
                    var project = await context.Projects.AsNoTracking()
                        .OrderBy(item => item.Id)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (project is not null)
                    {
                        projectId = project.Id;
                        projectTitle = project.Title;
                    }

                    if (await TableExistsAsync(context, "Chapters", cancellationToken).ConfigureAwait(false))
                    {
                        var chapters = await context.Chapters.AsNoTracking()
                            .Select(item => item.RelativeMarkdownPath)
                            .ToListAsync(cancellationToken)
                            .ConfigureAwait(false);
                        foreach (var relative in chapters)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var absolute = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                            if (!File.Exists(absolute))
                            {
                                missingChapters.Add(relative.Replace('\\', '/'));
                            }
                        }
                    }
                }
                else
                {
                    notes.Add("Database exists but is not structurally readable.");
                }
            }
            catch (Exception ex)
            {
                notes.Add(SanitizeDiagnostic($"Database inspection failed: {ex.Message}"));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
        }
        else if (databaseExists && !integrity.IsHealthy)
        {
            notes.Add("SQLite integrity check failed. Automatic database repair is not attempted.");
        }

        if (metadataExists && projectId is null)
        {
            try
            {
                var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
                var document = JsonSerializer.Deserialize<ProjectMetadataDocument>(json, MetadataJsonOptions);
                if (document is not null)
                {
                    projectId = document.Id == Guid.Empty ? projectId : document.Id;
                    projectTitle ??= document.Title;
                }
            }
            catch (Exception ex)
            {
                notes.Add(SanitizeDiagnostic($"Metadata could not be parsed: {ex.Message}"));
            }
        }

        progress?.Report(new OperationProgress { Message = "Scanning snapshots…", PercentComplete = 55 });
        var active = await ListSnapshotsInDirectoryAsync(
                Path.Combine(root, "13 Archive", "Snapshots"),
                retired: false,
                cancellationToken)
            .ConfigureAwait(false);
        var retired = await ListSnapshotsInDirectoryAsync(
                Path.Combine(root, "13 Archive", "Snapshots", SnapshotRetentionPolicy.RetiredDirectoryName),
                retired: true,
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new OperationProgress { Message = "Scanning recovery journal…", PercentComplete = 80 });
        var journals = await ListJournalEntriesAsync(root, projectId, cancellationToken).ConfigureAwait(false);

        progress?.Report(new OperationProgress { Message = "Diagnostics complete", PercentComplete = 100 });
        return new RecoveryCentreReport
        {
            ProjectRootPath = root,
            ProjectId = projectId,
            ProjectTitle = projectTitle,
            DatabaseExists = databaseExists,
            MetadataExists = metadataExists,
            Integrity = integrity,
            DatabaseReadable = databaseReadable,
            MissingChapterRelativePaths = missingChapters,
            DiagnosticNotes = notes,
            ActiveSnapshots = active,
            RetiredSnapshots = retired,
            JournalEntries = journals,
        };
    }

    public async Task<SnapshotRestorePreview> PreviewSnapshotAsync(
        string projectRootPath,
        string snapshotDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        _ = Path.GetFullPath(projectRootPath);
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

    public async Task<RecoveryActionResult> RestoreSnapshotAsync(
        string projectRootPath,
        string snapshotDirectoryPath,
        bool confirmed,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        if (!confirmed)
        {
            return new RecoveryActionResult
            {
                Succeeded = false,
                Message = "Restore cancelled: confirmation is required.",
            };
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(projectRootPath);
            var preview = await PreviewSnapshotAsync(root, snapshotDirectoryPath, cancellationToken)
                .ConfigureAwait(false);
            if (!preview.CanRestore)
            {
                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = "Snapshot validation failed.",
                };
            }

            progress?.Report(new OperationProgress
            {
                Message = "Creating protection snapshot…",
                PercentComplete = 10,
            });
            await ProtectProjectAsync(root, "safety-restore", cancellationToken, progress)
                .ConfigureAwait(false);

            if (_projects.ActiveProject is { } active
                && string.Equals(active.RootPath, root, StringComparison.OrdinalIgnoreCase))
            {
                await _projects.CloseAsync(cancellationToken).ConfigureAwait(false);
            }

            SqliteConnection.ClearAllPools();
            var staging = Path.Combine(Path.GetTempPath(), "mbws-recovery-restore-" + Guid.NewGuid().ToString("N"));
            var backupOfCurrent = Path.Combine(Path.GetTempPath(), "mbws-recovery-current-" + Guid.NewGuid().ToString("N"));
            try
            {
                progress?.Report(new OperationProgress { Message = "Staging snapshot…", PercentComplete = 30 });
                Directory.CreateDirectory(staging);
                await CopyDirectoryAsync(snapshotDirectoryPath, staging, cancellationToken).ConfigureAwait(false);
                var staged = await InspectSnapshotAsync(staging, cancellationToken).ConfigureAwait(false);
                if (!staged.IsValid)
                {
                    return new RecoveryActionResult
                    {
                        Succeeded = false,
                        Message = "Staged snapshot failed validation.",
                    };
                }

                progress?.Report(new OperationProgress { Message = "Backing up current files…", PercentComplete = 50 });
                Directory.CreateDirectory(backupOfCurrent);
                await CopyRestorableTreeAsync(root, backupOfCurrent, cancellationToken).ConfigureAwait(false);

                progress?.Report(new OperationProgress { Message = "Applying restored files…", PercentComplete = 70 });
                ClearRestorableContent(root);
                foreach (var entry in staged.Manifest.Files)
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

                progress?.Report(new OperationProgress { Message = "Restore complete", PercentComplete = 100 });
                return new RecoveryActionResult
                {
                    Succeeded = true,
                    Message = $"Restored snapshot '{preview.SnapshotName}' ({preview.FileCount} files).",
                };
            }
            catch (Exception ex)
            {
                try
                {
                    SqliteConnection.ClearAllPools();
                    if (Directory.Exists(backupOfCurrent))
                    {
                        ClearRestorableContent(root);
                        await CopyDirectoryAsync(backupOfCurrent, root, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Preserve original error.
                }

                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = SanitizeDiagnostic($"Restore failed: {ex.Message}"),
                };
            }
            finally
            {
                TryDelete(staging);
                TryDelete(backupOfCurrent);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecoveryActionResult> UnretireSnapshotAsync(
        string projectRootPath,
        string retiredSnapshotDirectoryPath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(projectRootPath);
            var source = Path.GetFullPath(retiredSnapshotDirectoryPath);
            var retiredRoot = Path.Combine(root, "13 Archive", "Snapshots", SnapshotRetentionPolicy.RetiredDirectoryName);
            if (!source.StartsWith(retiredRoot, StringComparison.OrdinalIgnoreCase))
            {
                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = "Selected folder is not under the Retired snapshots directory.",
                };
            }

            if (!Directory.Exists(source))
            {
                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = "Retired snapshot folder was not found.",
                };
            }

            var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var destination = Path.Combine(root, "13 Archive", "Snapshots", name);
            if (Directory.Exists(destination))
            {
                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = $"Cannot unretire '{name}' because an active snapshot with that name already exists.",
                };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(source, destination);
            return new RecoveryActionResult
            {
                Succeeded = true,
                Message = $"Moved '{name}' back to the active snapshot list.",
                OutputPath = destination,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecoveryJournalPreview?> PreviewJournalAsync(
        string projectRootPath,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(projectRootPath);
        var path = Path.Combine(RecoveryPaths.GetJournalDirectory(root), $"{entryId:N}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var entry = await ReadJournalEntryAsync(path, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        return new RecoveryJournalPreview
        {
            EntryId = entry.EntryId,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            SecondaryKey = entry.SecondaryKey,
            UpdatedUtc = entry.UpdatedUtc,
            CharacterCount = entry.DraftText.Length,
            DraftText = entry.DraftText,
        };
    }

    public async Task<RecoveryActionResult> RecoverJournalToCopyAsync(
        string projectRootPath,
        Guid entryId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(projectRootPath);
            var preview = await PreviewJournalAsync(root, entryId, cancellationToken).ConfigureAwait(false);
            if (preview is null)
            {
                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = "Journal entry was not found.",
                };
            }

            progress?.Report(new OperationProgress
            {
                Message = "Writing recovered copy…",
                PercentComplete = 40,
            });

            var recoveredDir = Path.Combine(root, "13 Archive", "Recovery", "Recovered");
            Directory.CreateDirectory(recoveredDir);
            var stamp = _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss");
            var fileName = $"{stamp}-{preview.EntityType}-{preview.EntryId:N}.md";
            var absolute = Path.Combine(recoveredDir, fileName);
            if (File.Exists(absolute))
            {
                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = "A recovered copy with that name already exists.",
                };
            }

            var header =
                $"<!-- Recovered journal copy. EntityType={preview.EntityType}; EntityId={preview.EntityId:N}; SecondaryKey={preview.SecondaryKey ?? "-"} -->{Environment.NewLine}";
            await AtomicFileWriter
                .WriteAllTextAsync(absolute, header + preview.DraftText, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new OperationProgress { Message = "Recovered copy written", PercentComplete = 100 });
            var relative = Path.GetRelativePath(root, absolute).Replace('\\', '/');
            return new RecoveryActionResult
            {
                Succeeded = true,
                Message = $"Wrote recovered copy ({preview.CharacterCount} characters) to {relative}.",
                OutputPath = absolute,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecoveryActionResult> RecoverJournalOverwriteAsync(
        string projectRootPath,
        Guid entryId,
        bool firstConfirmation,
        bool secondConfirmation,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        if (!firstConfirmation || !secondConfirmation)
        {
            return new RecoveryActionResult
            {
                Succeeded = false,
                Message = "Overwrite cancelled: two confirmations are required.",
            };
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(projectRootPath);
            var preview = await PreviewJournalAsync(root, entryId, cancellationToken).ConfigureAwait(false);
            if (preview is null)
            {
                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = "Journal entry was not found.",
                };
            }

            progress?.Report(new OperationProgress
            {
                Message = "Creating protection snapshot…",
                PercentComplete = 15,
            });
            await ProtectProjectAsync(root, "safety-journal", cancellationToken, progress)
                .ConfigureAwait(false);

            progress?.Report(new OperationProgress
            {
                Message = "Applying journal overwrite…",
                PercentComplete = 60,
            });

            switch (preview.EntityType)
            {
                case RecoveryEntityType.ManuscriptChapter:
                    await OverwriteChapterAsync(root, preview, cancellationToken).ConfigureAwait(false);
                    break;
                case RecoveryEntityType.DocumentField:
                case RecoveryEntityType.DocumentNotes:
                    await OverwriteDocumentAsync(root, preview, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    return new RecoveryActionResult
                    {
                        Succeeded = false,
                        Message = "Unsupported journal entity type.",
                    };
            }

            progress?.Report(new OperationProgress { Message = "Overwrite complete", PercentComplete = 100 });
            return new RecoveryActionResult
            {
                Succeeded = true,
                Message = $"Overwrote current {preview.EntityType} content from journal entry.",
            };
        }
        catch (Exception ex)
        {
            return new RecoveryActionResult
            {
                Succeeded = false,
                Message = SanitizeDiagnostic($"Journal overwrite failed: {ex.Message}"),
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecoveryActionResult> RebuildMetadataAsync(
        string projectRootPath,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(projectRootPath);
            var databasePath = Path.Combine(root, ProjectPaths.DatabaseFileName);
            var metadataPath = Path.Combine(root, ProjectPaths.MetadataFileName);
            if (!File.Exists(databasePath))
            {
                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = $"Cannot rebuild metadata without {ProjectPaths.DatabaseFileName}.",
                };
            }

            if (File.Exists(metadataPath))
            {
                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = $"{ProjectPaths.MetadataFileName} already exists. Rebuild is only for missing metadata.",
                };
            }

            var integrity = await _integrity.CheckAsync(root, cancellationToken).ConfigureAwait(false);
            if (!integrity.IsHealthy)
            {
                return new RecoveryActionResult
                {
                    Succeeded = false,
                    Message = "Cannot rebuild metadata from an unhealthy database.",
                };
            }

            progress?.Report(new OperationProgress
            {
                Message = "Reading project record…",
                PercentComplete = 30,
            });

            ProjectRecord record;
            int schemaVersion;
            await using (var context = ProjectDbContextFactory.Create(databasePath))
            {
                record = await context.Projects.AsNoTracking()
                        .OrderBy(item => item.Id)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("No project record was found in the database.");
                schemaVersion = await context.SchemaVersions.AsNoTracking()
                    .OrderByDescending(item => item.Version)
                    .Select(item => item.Version)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            SqliteConnection.ClearAllPools();
            var document = new ProjectMetadataDocument
            {
                Id = record.Id,
                Title = record.Title,
                Author = record.Author,
                Genre = record.Genre,
                PublishingRoute = record.PublishingRoute,
                NorthStar = record.NorthStar,
                SchemaVersion = schemaVersion == 0 ? ProjectSchema.CurrentVersion : schemaVersion,
                CreatedUtc = record.CreatedUtc,
                LastEditedUtc = record.LastEditedUtc,
            };

            progress?.Report(new OperationProgress
            {
                Message = "Writing project.json…",
                PercentComplete = 80,
            });
            await AtomicFileWriter
                .WriteAllTextAsync(
                    metadataPath,
                    JsonSerializer.Serialize(document, MetadataJsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new OperationProgress { Message = "Metadata rebuilt", PercentComplete = 100 });
            return new RecoveryActionResult
            {
                Succeeded = true,
                Message = $"Rebuilt {ProjectPaths.MetadataFileName} from the database.",
                OutputPath = metadataPath,
            };
        }
        catch (Exception ex)
        {
            return new RecoveryActionResult
            {
                Succeeded = false,
                Message = SanitizeDiagnostic($"Metadata rebuild failed: {ex.Message}"),
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ProtectProjectAsync(
        string root,
        string namePrefix,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        var identity = await ResolveIdentityAsync(root, cancellationToken).ConfigureAwait(false);
        try
        {
            await _snapshotWriter.WriteAsync(
                    new ProjectSnapshotWriteRequest
                    {
                        ProjectRootPath = root,
                        ProjectId = identity.Id,
                        ProjectTitle = identity.Title,
                        SchemaVersion = identity.SchemaVersion,
                        Kind = SnapshotKind.Safety,
                        NamePrefix = namePrefix,
                    },
                    cancellationToken,
                    progress)
                .ConfigureAwait(false);
        }
        catch
        {
            // Fall back to a best-effort file tree protection copy when SQLite backup fails.
            await WriteFallbackProtectionAsync(root, namePrefix, identity, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task WriteFallbackProtectionAsync(
        string root,
        string namePrefix,
        (Guid Id, string Title, int SchemaVersion) identity,
        CancellationToken cancellationToken)
    {
        var stamp = _timeProvider.GetLocalNow().ToString("yyyyMMdd-HHmmss-fff");
        var name = $"{namePrefix}-files-{stamp}";
        var dest = Path.Combine(root, "13 Archive", "Snapshots", name);
        if (Directory.Exists(dest))
        {
            throw new InvalidOperationException($"Protection snapshot already exists: {name}");
        }

        Directory.CreateDirectory(dest);
        var files = new List<SnapshotFileEntry>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (BackupPathRules.IsExcludedRelativePath(relative))
            {
                continue;
            }

            var target = Path.Combine(dest, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
            files.Add(new SnapshotFileEntry
            {
                RelativePath = relative,
                SizeBytes = new FileInfo(target).Length,
                Sha256 = await ChecksumHelper.Sha256FileAsync(target, cancellationToken).ConfigureAwait(false),
            });
        }

        var manifest = new SnapshotManifest
        {
            FormatVersion = SnapshotManifest.CurrentFormatVersion,
            ProjectId = identity.Id,
            ProjectTitle = identity.Title,
            CreatedUtc = _timeProvider.GetUtcNow(),
            SchemaVersion = identity.SchemaVersion,
            SnapshotName = name,
            Kind = SnapshotKind.Safety,
            Files = files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
        };
        await AtomicFileWriter
            .WriteAllTextAsync(
                Path.Combine(dest, "snapshot-manifest.json"),
                ManifestSerializer.Serialize(manifest),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(Guid Id, string Title, int SchemaVersion)> ResolveIdentityAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(root, ProjectPaths.MetadataFileName);
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

        var databasePath = Path.Combine(root, ProjectPaths.DatabaseFileName);
        if (File.Exists(databasePath))
        {
            try
            {
                await using var context = ProjectDbContextFactory.Create(databasePath);
                var record = await context.Projects.AsNoTracking()
                    .OrderBy(item => item.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (record is not null)
                {
                    var schema = await context.SchemaVersions.AsNoTracking()
                        .OrderByDescending(item => item.Version)
                        .Select(item => item.Version)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                    return (record.Id, record.Title, schema);
                }
            }
            catch
            {
                // Fall through.
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
        }

        return (
            Guid.Empty,
            Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            0);
    }

    private async Task OverwriteChapterAsync(
        string root,
        RecoveryJournalPreview preview,
        CancellationToken cancellationToken)
    {
        var databasePath = Path.Combine(root, ProjectPaths.DatabaseFileName);
        string relativePath;
        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            var chapter = await context.Chapters.AsNoTracking()
                    .FirstOrDefaultAsync(item => item.Id == preview.EntityId, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Chapter record was not found for journal overwrite.");
            relativePath = chapter.RelativeMarkdownPath;
        }

        SqliteConnection.ClearAllPools();
        var absolute = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        await AtomicFileWriter.WriteAllTextAsync(absolute, preview.DraftText, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task OverwriteDocumentAsync(
        string root,
        RecoveryJournalPreview preview,
        CancellationToken cancellationToken)
    {
        var databasePath = Path.Combine(root, ProjectPaths.DatabaseFileName);
        await using var context = ProjectDbContextFactory.Create(databasePath);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var document = await context.Documents
                .Include(item => item.Fields)
                .FirstOrDefaultAsync(item => item.Id == preview.EntityId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Document record was not found for journal overwrite.");

        if (preview.EntityType == RecoveryEntityType.DocumentNotes)
        {
            document.Notes = preview.DraftText;
        }
        else
        {
            var field = document.Fields.FirstOrDefault(item => item.Key == preview.SecondaryKey)
                ?? throw new InvalidOperationException("Document field was not found for journal overwrite.");
            field.Value = preview.DraftText;
        }

        document.LastEditedUtc = _timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        SqliteConnection.ClearAllPools();
    }

    private static async Task<IReadOnlyList<RecoverySnapshotItem>> ListSnapshotsInDirectoryAsync(
        string directory,
        bool retired,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var results = new List<RecoverySnapshotItem>();
        foreach (var path in Directory.GetDirectories(directory)
                     .Where(item => retired
                         || !SnapshotRetentionPolicy.IsRetiredDirectoryName(Path.GetFileName(item)))
                     .OrderByDescending(item => item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = await InspectSnapshotAsync(path, cancellationToken).ConfigureAwait(false);
            results.Add(new RecoverySnapshotItem
            {
                Name = info.Name,
                DirectoryPath = info.DirectoryPath,
                IsValid = info.IsValid,
                IsRetired = retired,
                CreatedUtc = info.Manifest.CreatedUtc == default
                    ? Directory.GetCreationTimeUtc(path)
                    : info.Manifest.CreatedUtc,
                FileCount = info.Manifest.Files.Count,
                ValidationErrors = info.ValidationErrors,
            });
        }

        return results;
    }

    private static async Task<IReadOnlyList<RecoveryJournalInfo>> ListJournalEntriesAsync(
        string root,
        Guid? expectedProjectId,
        CancellationToken cancellationToken)
    {
        var directory = RecoveryPaths.GetJournalDirectory(root);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var results = new List<RecoveryJournalInfo>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await ReadJournalEntryAsync(path, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                continue;
            }

            if (expectedProjectId is { } projectId && entry.ProjectId != Guid.Empty && entry.ProjectId != projectId)
            {
                continue;
            }

            results.Add(new RecoveryJournalInfo
            {
                EntryId = entry.EntryId,
                ProjectId = entry.ProjectId,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                SecondaryKey = entry.SecondaryKey,
                CreatedUtc = entry.CreatedUtc,
                UpdatedUtc = entry.UpdatedUtc,
                AbsolutePath = path,
                DraftLength = entry.DraftText.Length,
            });
        }

        return results.OrderByDescending(item => item.UpdatedUtc).ToList();
    }

    private static async Task<SnapshotInfo> InspectSnapshotAsync(string directory, CancellationToken cancellationToken)
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
                ValidationErrors = [$"Invalid manifest: {SanitizeDiagnostic(ex.Message)}"],
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

    private static async Task<RecoveryJournalEntry?> ReadJournalEntryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<RecoveryJournalEntry>(json, JournalJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> TableExistsAsync(
        ProjectDbContext context,
        string tableName,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result) > 0;
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

    private static string SanitizeDiagnostic(string message)
    {
        // Keep diagnostics free of long prose/payloads.
        if (message.Length <= 240)
        {
            return message;
        }

        return message[..237] + "...";
    }
}
