using System.Text.Json;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Infrastructure.Documents;
using MasterBookWritingSystem.Infrastructure.IO;
using MasterBookWritingSystem.Infrastructure.Manuscript;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Persistence;

public sealed class ProjectService : IProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IApplicationPaths _applicationPaths;
    private readonly IWorkflowDefinitionSource _workflowDefinitionSource;
    private readonly IDocumentTemplateCatalog _documentTemplateCatalog;
    private readonly IProjectIntegrityService _integrityService;
    private readonly IProjectSnapshotWriter _snapshotWriter;
    private readonly object _gate = new();
    private Project? _activeProject;

    public ProjectService(
        IApplicationPaths applicationPaths,
        IWorkflowDefinitionSource workflowDefinitionSource,
        IDocumentTemplateCatalog documentTemplateCatalog,
        IProjectIntegrityService integrityService,
        IProjectSnapshotWriter snapshotWriter)
    {
        _applicationPaths = applicationPaths;
        _workflowDefinitionSource = workflowDefinitionSource;
        _documentTemplateCatalog = documentTemplateCatalog;
        _integrityService = integrityService;
        _snapshotWriter = snapshotWriter;
    }

    public Project? ActiveProject
    {
        get
        {
            lock (_gate)
            {
                return _activeProject;
            }
        }
    }

    public async Task<Project> CreateAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ParentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);

        var parentDirectory = Path.GetFullPath(request.ParentDirectory);
        EnsureNotUnderLocalAppData(parentDirectory);

        var folderName = SanitizeFolderName(request.Title);
        var rootPath = Path.Combine(parentDirectory, folderName);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);
        var metadataPath = Path.Combine(rootPath, ProjectPaths.MetadataFileName);

        if (Directory.Exists(rootPath)
            && (File.Exists(databasePath) || File.Exists(metadataPath)))
        {
            throw new ProjectAlreadyExistsException(
                $"A project already exists at '{rootPath}'. Choose a different title or location.");
        }

        Directory.CreateDirectory(rootPath);
        foreach (var relativeDirectory in ProjectPaths.StandardDirectories)
        {
            Directory.CreateDirectory(Path.Combine(rootPath, relativeDirectory));
        }

        var now = DateTimeOffset.UtcNow;
        var record = new ProjectRecord
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Author = request.Author.Trim(),
            Genre = request.Genre.Trim(),
            NorthStar = request.NorthStar.Trim(),
            PublishingRoute = PublishingRoute.Unspecified,
            CreatedUtc = now,
            LastEditedUtc = now,
        };

        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            context.Projects.Add(record);
            context.SchemaVersions.Add(new SchemaVersionRecord
            {
                Version = ProjectSchema.CurrentVersion,
                Name = ProjectSchema.DraftingProgressMigrationName,
                AppliedUtc = now,
            });

            var definitions = await _workflowDefinitionSource
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            await WorkflowDefinitionImporter
                .ImportAsync(context, record.Id, definitions, cancellationToken)
                .ConfigureAwait(false);

            var documentTemplates = await _documentTemplateCatalog
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(false);
            await DocumentSeeder
                .SeedAsync(
                    context,
                    record.Id,
                    rootPath,
                    documentTemplates,
                    DocumentSeeder.FindSeedRoot(),
                    cancellationToken)
                .ConfigureAwait(false);

            await ManuscriptHierarchyBootstrap
                .EnsureAsync(context, record.Id, record.Title, cancellationToken)
                .ConfigureAwait(false);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();

        var project = ToDomain(record, rootPath);
        await WriteMetadataAsync(project, cancellationToken).ConfigureAwait(false);
        SetActive(project);
        return project;
    }

    public async Task<Project> OpenAsync(
        string projectRootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);

        var rootPath = Path.GetFullPath(projectRootPath);
        var validation = await ValidateAsync(rootPath, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new ProjectValidationException(string.Join(" ", validation.Errors));
        }

        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);
        ProjectRecord record;

        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            var pending = (await context.Database
                    .GetPendingMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false))
                .ToList();
            if (pending.Count > 0)
            {
                await CreatePreMigrationSafetySnapshotAsync(rootPath, context, cancellationToken)
                    .ConfigureAwait(false);
            }

            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            record = await context.Projects
                .OrderBy(project => project.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ProjectNotFoundException($"No project record was found in '{databasePath}'.");

            // Projects created before Milestone 5 need the 24 structured documents after schema migration.
            var documentTemplates = await _documentTemplateCatalog
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(false);
            await DocumentSeeder
                .SeedAsync(
                    context,
                    record.Id,
                    rootPath,
                    documentTemplates,
                    DocumentSeeder.FindSeedRoot(),
                    cancellationToken)
                .ConfigureAwait(false);

            await ManuscriptHierarchyBootstrap
                .EnsureAsync(context, record.Id, record.Title, cancellationToken)
                .ConfigureAwait(false);

            if (!await context.SchemaVersions
                    .AnyAsync(item => item.Version == ProjectSchema.CurrentVersion, cancellationToken)
                    .ConfigureAwait(false))
            {
                context.SchemaVersions.Add(new SchemaVersionRecord
                {
                    Version = ProjectSchema.CurrentVersion,
                    Name = ProjectSchema.DraftingProgressMigrationName,
                    AppliedUtc = DateTimeOffset.UtcNow,
                });
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        SqliteConnection.ClearAllPools();

        var project = ToDomain(record, rootPath);
        project.LastEditedUtc = DateTimeOffset.UtcNow;
        await WriteMetadataAsync(project, cancellationToken).ConfigureAwait(false);
        SetActive(project);
        return project;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _activeProject = null;
        }

        SqliteConnection.ClearAllPools();
        return Task.CompletedTask;
    }

    public async Task<ProjectValidationResult> ValidateAsync(
        string projectRootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);

        var errors = new List<string>();
        var rootPath = Path.GetFullPath(projectRootPath);

        if (!Directory.Exists(rootPath))
        {
            errors.Add($"Project folder was not found: {rootPath}");
            return new ProjectValidationResult { IsValid = false, Errors = errors };
        }

        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);
        var metadataPath = Path.Combine(rootPath, ProjectPaths.MetadataFileName);

        if (!File.Exists(databasePath))
        {
            errors.Add($"Missing {ProjectPaths.DatabaseFileName}.");
        }

        if (!File.Exists(metadataPath))
        {
            errors.Add($"Missing {ProjectPaths.MetadataFileName}.");
        }

        var schemaVersion = 0;
        if (File.Exists(databasePath))
        {
            var integrity = await _integrityService.CheckAsync(rootPath, cancellationToken)
                .ConfigureAwait(false);
            if (!integrity.IsHealthy)
            {
                errors.Add(integrity.Summary);
                errors.AddRange(integrity.Details.Select(detail => $"SQLite: {detail}"));
            }
            else
            {
                try
                {
                    await using var context = ProjectDbContextFactory.Create(databasePath);
                    var canConnect = await context.Database
                        .CanConnectAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (!canConnect)
                    {
                        errors.Add($"{ProjectPaths.DatabaseFileName} is not a readable SQLite database.");
                    }
                    else
                    {
                        var hasProjectsTable = await TableExistsAsync(context, "Projects", cancellationToken)
                            .ConfigureAwait(false);
                        if (!hasProjectsTable)
                        {
                            errors.Add("Database schema has not been initialized.");
                        }
                        else
                        {
                            schemaVersion = await context.SchemaVersions
                                .AsNoTracking()
                                .OrderByDescending(version => version.Version)
                                .Select(version => version.Version)
                                .FirstOrDefaultAsync(cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to inspect {ProjectPaths.DatabaseFileName}: {ex.Message}");
                }
                finally
                {
                    SqliteConnection.ClearAllPools();
                }
            }
        }

        return new ProjectValidationResult
        {
            IsValid = errors.Count == 0,
            SchemaVersion = schemaVersion == 0 && errors.Count == 0
                ? ProjectSchema.CurrentVersion
                : schemaVersion,
            Errors = errors,
        };
    }

    private async Task CreatePreMigrationSafetySnapshotAsync(
        string rootPath,
        ProjectDbContext context,
        CancellationToken cancellationToken)
    {
        var identity = await TryResolveProjectIdentityAsync(rootPath, context, cancellationToken)
            .ConfigureAwait(false);
        if (identity is null)
        {
            return;
        }

        await _snapshotWriter.WriteAsync(
                new ProjectSnapshotWriteRequest
                {
                    ProjectRootPath = rootPath,
                    ProjectId = identity.Value.Id,
                    ProjectTitle = identity.Value.Title,
                    SchemaVersion = identity.Value.SchemaVersion,
                    Kind = SnapshotKind.Safety,
                    NamePrefix = "safety-migration",
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(Guid Id, string Title, int SchemaVersion)?> TryResolveProjectIdentityAsync(
        string rootPath,
        ProjectDbContext context,
        CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(rootPath, ProjectPaths.MetadataFileName);
        if (File.Exists(metadataPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
                var document = JsonSerializer.Deserialize<ProjectMetadataDocument>(json, JsonOptions);
                if (document is not null && document.Id != Guid.Empty)
                {
                    return (document.Id, document.Title, document.SchemaVersion);
                }
            }
            catch
            {
                // Fall through to database lookup.
            }
        }

        if (!await TableExistsAsync(context, "Projects", cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var record = await context.Projects
            .AsNoTracking()
            .OrderBy(project => project.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var schemaVersion = 0;
        if (await TableExistsAsync(context, "SchemaVersions", cancellationToken).ConfigureAwait(false))
        {
            schemaVersion = await context.SchemaVersions
                .AsNoTracking()
                .OrderByDescending(version => version.Version)
                .Select(version => version.Version)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return (record.Id, record.Title, schemaVersion);
    }

    private void SetActive(Project project)
    {
        lock (_gate)
        {
            _activeProject = project;
        }
    }

    private void EnsureNotUnderLocalAppData(string path)
    {
        var localAppData = Path.GetFullPath(_applicationPaths.LocalAppDataDirectory);
        var candidate = Path.GetFullPath(path);

        if (candidate.Equals(localAppData, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(
                localAppData.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidProjectLocationException(
                "Book projects cannot be stored under the application LocalAppData directory. Choose a portable folder.");
        }
    }

    private static async Task WriteMetadataAsync(Project project, CancellationToken cancellationToken)
    {
        var document = new ProjectMetadataDocument
        {
            Id = project.Id,
            Title = project.Title,
            Author = project.Author,
            Genre = project.Genre,
            PublishingRoute = project.PublishingRoute,
            NorthStar = project.NorthStar,
            SchemaVersion = ProjectSchema.CurrentVersion,
            CreatedUtc = project.CreatedUtc,
            LastEditedUtc = project.LastEditedUtc,
        };

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var metadataPath = Path.Combine(project.RootPath, ProjectPaths.MetadataFileName);
        await AtomicFileWriter.WriteAllTextAsync(metadataPath, json, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Project ToDomain(ProjectRecord record, string rootPath) => new()
    {
        Id = record.Id,
        Title = record.Title,
        Author = record.Author,
        Genre = record.Genre,
        PublishingRoute = record.PublishingRoute,
        NorthStar = record.NorthStar,
        RootPath = rootPath,
        CreatedUtc = record.CreatedUtc,
        LastEditedUtc = record.LastEditedUtc,
    };

    private static string SanitizeFolderName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled Project" : cleaned;
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
}
