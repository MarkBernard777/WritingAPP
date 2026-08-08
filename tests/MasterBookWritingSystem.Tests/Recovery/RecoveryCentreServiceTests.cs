using System.Text;
using System.Text.Json;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Recovery;

public sealed class RecoveryCentreServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly ISnapshotService _snapshots;
    private readonly IRecoveryJournalService _journal;
    private readonly IRecoveryCentreService _recovery;

    public RecoveryCentreServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-recovery-centre-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPaths>(new TempPaths(Path.Combine(_tempRoot, "appdata")));
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _snapshots = _provider.GetRequiredService<ISnapshotService>();
        _journal = _provider.GetRequiredService<IRecoveryJournalService>();
        _recovery = _provider.GetRequiredService<IRecoveryCentreService>();
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
    public async Task Diagnose_ReportsCorruptDatabase_WithoutActiveProject()
    {
        var project = await CreateAsync("Corrupt");
        var root = project.RootPath;
        await _projects.CloseAsync();
        SqliteConnection.ClearAllPools();
        await File.WriteAllBytesAsync(
            Path.Combine(root, ProjectPaths.DatabaseFileName),
            Encoding.UTF8.GetBytes("not-a-database"));

        Assert.Null(_projects.ActiveProject);
        var report = await _recovery.DiagnoseAsync(root);
        Assert.False(report.Integrity.IsHealthy);
        Assert.True(report.DatabaseExists);
        Assert.False(report.DatabaseReadable);
        Assert.Contains(
            report.DiagnosticNotes,
            note => note.Contains("integrity", StringComparison.OrdinalIgnoreCase)
                || note.Contains("Automatic database repair", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            report.DiagnosticNotes,
            note => note.Contains("manuscript prose", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RebuildMetadata_RecreatesMissingProjectJson()
    {
        var project = await CreateAsync("Meta");
        var root = project.RootPath;
        var metadataPath = Path.Combine(root, ProjectPaths.MetadataFileName);
        await _projects.CloseAsync();
        File.Delete(metadataPath);
        Assert.False(File.Exists(metadataPath));

        var result = await _recovery.RebuildMetadataAsync(root);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.Exists(metadataPath));

        var report = await _recovery.DiagnoseAsync(root);
        Assert.True(report.MetadataExists);
        Assert.Equal(project.Id, report.ProjectId);
    }

    [Fact]
    public async Task Diagnose_ReportsMissingChapterFile()
    {
        var project = await CreateAsync("MissingChapter");
        var chapter = await _chapters.CreateAsync(project.Id, "Gone");
        var absolute = Path.Combine(project.RootPath, chapter.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(absolute);
        await _projects.CloseAsync();

        var report = await _recovery.DiagnoseAsync(project.RootPath);
        Assert.Contains(
            report.MissingChapterRelativePaths,
            path => path.Equals(chapter.RelativeMarkdownPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Diagnose_ReportsInvalidSnapshot()
    {
        var project = await CreateAsync("BadSnap");
        await _snapshots.CreateSnapshotAsync(project.Id);
        var snapshotsRoot = Path.Combine(project.RootPath, "13 Archive", "Snapshots");
        var snapshotDir = Directory.GetDirectories(snapshotsRoot)
            .First(path => !SnapshotRetentionPolicy.IsRetiredDirectoryName(Path.GetFileName(path)));
        File.Delete(Path.Combine(snapshotDir, "snapshot-manifest.json"));
        await _projects.CloseAsync();

        var report = await _recovery.DiagnoseAsync(project.RootPath);
        Assert.Contains(report.ActiveSnapshots, item => !item.IsValid);
    }

    [Fact]
    public async Task JournalPreview_AndRecoveredCopy_DoNotOverwriteOriginal()
    {
        var project = await CreateAsync("JournalCopy");
        var chapter = await _chapters.CreateAsync(project.Id, "Draft");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "original durable");
        var entryId = RecoveryJournalIds.Create(
            project.Id,
            RecoveryEntityType.ManuscriptChapter,
            chapter.Id);
        await _journal.UpsertAsync(new RecoveryJournalEntry
        {
            EntryId = entryId,
            ProjectId = project.Id,
            EntityType = RecoveryEntityType.ManuscriptChapter,
            EntityId = chapter.Id,
            DraftText = "recovered draft body",
        });
        await _projects.CloseAsync();

        var preview = await _recovery.PreviewJournalAsync(project.RootPath, entryId);
        Assert.NotNull(preview);
        Assert.Equal("recovered draft body", preview!.DraftText);
        Assert.Equal("recovered draft body".Length, preview.CharacterCount);

        var result = await _recovery.RecoverJournalToCopyAsync(project.RootPath, entryId);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal("original durable", await File.ReadAllTextAsync(
            Path.Combine(project.RootPath, chapter.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Contains("recovered draft body", await File.ReadAllTextAsync(result.OutputPath!));
    }

    [Fact]
    public async Task RestoreSnapshot_CancelledWithoutConfirmation()
    {
        var project = await CreateAsync("CancelRestore");
        var snapshot = await _snapshots.CreateSnapshotAsync(project.Id);
        await _projects.CloseAsync();

        var result = await _recovery.RestoreSnapshotAsync(
            project.RootPath,
            snapshot.DirectoryPath,
            confirmed: false);
        Assert.False(result.Succeeded);
        Assert.Contains("confirmation", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnretireSnapshot_MovesBackToActiveList()
    {
        var project = await CreateAsync("Unretire");
        var settings = _provider.GetRequiredService<IApplicationSettingsStore>();
        await settings.SaveAsync(new Core.Settings.ApplicationSettings { SnapshotRetentionLimit = 1 });
        var first = await _snapshots.CreateSnapshotAsync(project.Id);
        await Task.Delay(5);
        _ = await _snapshots.CreateSnapshotAsync(project.Id);
        var retiredPath = Path.Combine(
            project.RootPath,
            "13 Archive",
            "Snapshots",
            SnapshotRetentionPolicy.RetiredDirectoryName,
            first.Name);
        Assert.True(Directory.Exists(retiredPath));
        await _projects.CloseAsync();

        var result = await _recovery.UnretireSnapshotAsync(project.RootPath, retiredPath);
        Assert.True(result.Succeeded, result.Message);
        var report = await _recovery.DiagnoseAsync(project.RootPath);
        Assert.Contains(report.ActiveSnapshots, item => item.Name == first.Name);
        Assert.DoesNotContain(report.RetiredSnapshots, item => item.Name == first.Name);
    }

    private async Task<Core.Domain.Project> CreateAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);
        return await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title + "-" + Guid.NewGuid().ToString("N")[..8],
            Author = "Tester",
            Genre = "Fantasy",
            NorthStar = "Recover",
        });
    }

    private sealed class TempPaths(string root) : IApplicationPaths
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
