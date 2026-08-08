using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Settings;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Backup;

public sealed class SnapshotPolicyAndSafetyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _appDataRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly ISnapshotService _snapshots;
    private readonly IPortablePackageService _packages;
    private readonly IApplicationSettingsStore _settings;
    private readonly IExportService _exports;

    public SnapshotPolicyAndSafetyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-snapshot-policy-tests", Guid.NewGuid().ToString("N"));
        _appDataRoot = Path.Combine(_tempRoot, "appdata");
        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(_appDataRoot);

        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPaths>(new TempApplicationPaths(_appDataRoot));
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _snapshots = _provider.GetRequiredService<ISnapshotService>();
        _packages = _provider.GetRequiredService<IPortablePackageService>();
        _settings = _provider.GetRequiredService<IApplicationSettingsStore>();
        _exports = _provider.GetRequiredService<IExportService>();
    }

    public void Dispose()
    {
        _projects.CloseAsync().GetAwaiter().GetResult();
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Retention_MovesOldestActiveSnapshotsToRetired_AndExcludesThemFromList()
    {
        await _settings.SaveAsync(new ApplicationSettings { SnapshotRetentionLimit = 2 });
        var project = await CreateAsync("Retention");

        var first = await _snapshots.CreateSnapshotAsync(project.Id);
        await Task.Delay(5);
        var second = await _snapshots.CreateSnapshotAsync(project.Id);
        await Task.Delay(5);
        var third = await _snapshots.CreateSnapshotAsync(project.Id);

        var active = await _snapshots.ListSnapshotsAsync(project.Id);
        Assert.Equal(2, active.Count);
        Assert.DoesNotContain(active, item => item.Name == first.Name);
        Assert.Contains(active, item => item.Name == second.Name);
        Assert.Contains(active, item => item.Name == third.Name);

        var retiredPath = Path.Combine(
            project.RootPath,
            "13 Archive",
            "Snapshots",
            SnapshotRetentionPolicy.RetiredDirectoryName,
            first.Name);
        Assert.True(Directory.Exists(retiredPath));
        Assert.DoesNotContain(
            active,
            item => SnapshotRetentionPolicy.IsRetiredDirectoryName(Path.GetFileName(item.DirectoryPath)));
    }

    [Fact]
    public async Task PreMigration_CreatesSafetySnapshot_OnlyWhenPendingMigrationsExist()
    {
        var root = Path.Combine(_tempRoot, "migrate-project");
        Directory.CreateDirectory(root);
        foreach (var relative in ProjectPaths.StandardDirectories)
        {
            Directory.CreateDirectory(Path.Combine(root, relative));
        }

        var projectId = Guid.NewGuid();
        var dbPath = Path.Combine(root, ProjectPaths.DatabaseFileName);
        await using (var context = ProjectDbContextFactory.Create(dbPath))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(ProjectSchema.PublishingWorkflowMigrationName);
            context.Projects.Add(new ProjectRecord
            {
                Id = projectId,
                Title = "Legacy",
                Author = "Tester",
                Genre = "Fantasy",
                NorthStar = "Recover",
                PublishingRoute = PublishingRoute.SelfPublishing,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastEditedUtc = DateTimeOffset.UtcNow,
            });
            context.SchemaVersions.Add(new SchemaVersionRecord
            {
                Version = 6,
                Name = ProjectSchema.PublishingWorkflowMigrationName,
                AppliedUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();
        await File.WriteAllTextAsync(
            Path.Combine(root, ProjectPaths.MetadataFileName),
            $$"""
            {
              "id": "{{projectId}}",
              "title": "Legacy",
              "author": "Tester",
              "genre": "Fantasy",
              "publishingRoute": 1,
              "northStar": "Recover",
              "schemaVersion": 6,
              "createdUtc": "2026-01-01T00:00:00Z",
              "lastEditedUtc": "2026-01-01T00:00:00Z"
            }
            """);

        var opened = await _projects.OpenAsync(root);
        var afterMigration = await _snapshots.ListSnapshotsAsync(opened.Id);
        Assert.Contains(afterMigration, item =>
            item.Manifest.Kind == SnapshotKind.Safety
            && item.Name.StartsWith("safety-migration-", StringComparison.OrdinalIgnoreCase));

        await _projects.CloseAsync();
        var beforeReopen = Directory.GetDirectories(
                Path.Combine(root, "13 Archive", "Snapshots"))
            .Count(path => !SnapshotRetentionPolicy.IsRetiredDirectoryName(Path.GetFileName(path)));

        var reopened = await _projects.OpenAsync(root);
        var afterReopen = await _snapshots.ListSnapshotsAsync(reopened.Id);
        Assert.Equal(beforeReopen, afterReopen.Count);
        Assert.Equal(
            1,
            afterReopen.Count(item => item.Name.StartsWith("safety-migration-", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task PreRestore_CreatesSafetySnapshot_BeforeDestructiveRestore()
    {
        var project = await CreateAsync("RestoreProtect");
        var chapter = await _chapters.CreateAsync(project.Id, "Original");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "alpha");
        var baseline = await _snapshots.CreateSnapshotAsync(project.Id);

        await _chapters.SaveContentAsync(project.Id, chapter.Id, "beta-changed");
        var beforeRestore = (await _snapshots.ListSnapshotsAsync(project.Id)).Count;

        var result = await _snapshots.RestoreSnapshotAsync(
            project.Id,
            baseline.DirectoryPath,
            confirmed: true);
        Assert.True(result.Succeeded, result.Message);

        var after = await _snapshots.ListSnapshotsAsync(project.Id);
        Assert.True(after.Count > beforeRestore);
        Assert.Contains(after, item =>
            item.Manifest.Kind == SnapshotKind.Safety
            && item.Name.StartsWith("safety-restore-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PreImportOverwrite_CreatesSafetySnapshot_AndKeepsItAfterImport()
    {
        var project = await CreateAsync("ImportProtect");
        await _chapters.CreateAsync(project.Id, "KeepMe");
        var zip = await _packages.ExportZipAsync(project.Id);

        var other = await CreateAsync("ImportTarget");
        await _chapters.CreateAsync(other.Id, "WillBeReplaced");
        var targetRoot = other.RootPath;
        await _projects.CloseAsync();

        await _packages.ImportZipAsync(zip.AbsolutePath, targetRoot, overwriteExisting: true);

        var retiredOrActive = Directory.GetDirectories(
            Path.Combine(targetRoot, "13 Archive", "Snapshots"),
            "*",
            SearchOption.AllDirectories);
        Assert.Contains(
            retiredOrActive,
            path => Path.GetFileName(path).StartsWith("safety-import-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompilationExport_SkipsWhenUnchanged_AndCreatesWhenChanged()
    {
        var project = await CreateAsync("ExportProtect");
        var chapter = await _chapters.CreateAsync(project.Id, "Prose");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "draft");
        await _snapshots.CreateSnapshotAsync(project.Id);

        var skipped = await _snapshots.CreateSafetySnapshotAsync(
            project.Id,
            SafetySnapshotReason.CompilationOrExport);
        Assert.Equal(SafetySnapshotOutcome.SkippedNoChanges, skipped.Outcome);
        Assert.Single(await _snapshots.ListSnapshotsAsync(project.Id));

        await _chapters.SaveContentAsync(project.Id, chapter.Id, "draft revised");
        var created = await _snapshots.CreateSafetySnapshotAsync(
            project.Id,
            SafetySnapshotReason.CompilationOrExport);
        Assert.Equal(SafetySnapshotOutcome.Created, created.Outcome);
        Assert.Equal(2, (await _snapshots.ListSnapshotsAsync(project.Id)).Count);

        await _exports.ExportManuscriptDocxAsync(project.Id, [chapter.Id]);
        // Unchanged since the safety snapshot above → no additional active snapshot.
        Assert.Equal(2, (await _snapshots.ListSnapshotsAsync(project.Id)).Count);
    }

    [Fact]
    public async Task RestoreWithoutConfirmation_IsNoOpForSafetySnapshot()
    {
        var project = await CreateAsync("NoConfirm");
        var snapshot = await _snapshots.CreateSnapshotAsync(project.Id);
        var before = (await _snapshots.ListSnapshotsAsync(project.Id)).Count;

        var result = await _snapshots.RestoreSnapshotAsync(
            project.Id,
            snapshot.DirectoryPath,
            confirmed: false);
        Assert.False(result.Succeeded);
        Assert.Equal(before, (await _snapshots.ListSnapshotsAsync(project.Id)).Count);
    }

    private async Task<Project> CreateAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);
        return await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title + "-" + Guid.NewGuid().ToString("N")[..8],
            Author = "Tester",
            Genre = "Fantasy",
            NorthStar = "Finish",
        });
    }

    private sealed class TempApplicationPaths(string root) : IApplicationPaths
    {
        public string LocalAppDataDirectory { get; } = root;
    }

    private static string FindPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }
}
