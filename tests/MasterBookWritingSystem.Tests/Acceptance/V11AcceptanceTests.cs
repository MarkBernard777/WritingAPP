using System.Net.Http;
using System.Reflection;
using System.Text;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Diagnostics;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Workflow;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Core.Workflow;
using MasterBookWritingSystem.Core.Settings;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Acceptance;

/// <summary>
/// v1.1 acceptance scenarios required for release readiness.
/// </summary>
public sealed class V11AcceptanceTests : IDisposable
{
    private const string SecretDraft = "SECRET_MANUSCRIPT_force_close_draft_body_do_not_log";

    private readonly string _tempRoot;
    private ServiceProvider _provider = null!;
    private IProjectService _projects = null!;
    private IChapterService _chapters = null!;
    private ISnapshotService _snapshots = null!;
    private IRecoveryJournalService _journal = null!;
    private IRecoveryCentreService _recovery = null!;
    private ISaveStateService _saveState = null!;
    private IApplicationSettingsStore _settings = null!;
    private IWorkflowService _workflow = null!;
    private IExportService _exports = null!;

    public V11AcceptanceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-v11-acceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        RebuildProvider();
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
    public async Task ForceCloseDuringEdit_RecoversLatestJournaledContent()
    {
        var project = await CreateAsync("ForceClose");
        var chapter = await _chapters.CreateAsync(project.Id, "Chapter");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "durable baseline");

        var entryId = RecoveryJournalIds.Create(
            project.Id,
            RecoveryEntityType.ManuscriptChapter,
            chapter.Id);

        // Simulate journal-before-save then process kill before durable write completes.
        await _journal.UpsertAsync(new RecoveryJournalEntry
        {
            EntryId = entryId,
            ProjectId = project.Id,
            EntityType = RecoveryEntityType.ManuscriptChapter,
            EntityId = chapter.Id,
            DraftText = SecretDraft,
        });

        var root = project.RootPath;
        await _projects.CloseAsync();

        Assert.Equal(
            "durable baseline",
            (await File.ReadAllTextAsync(
                Path.Combine(root, chapter.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar)))).Trim());

        var reopened = await _projects.OpenAsync(root);
        await _saveState.RefreshRecoveryAvailabilityAsync(reopened.Id);
        Assert.Equal(SaveState.RecoveryAvailable, _saveState.State);
        Assert.True(await _journal.HasRecoverableEntriesAsync(reopened.Id));

        var recovered = await _recovery.RecoverJournalToCopyAsync(root, entryId);
        Assert.True(recovered.Succeeded, recovered.Message);
        Assert.Contains(SecretDraft, await File.ReadAllTextAsync(recovered.OutputPath!), StringComparison.Ordinal);
        Assert.DoesNotContain(
            SecretDraft,
            await File.ReadAllTextAsync(
                Path.Combine(root, chapter.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar))),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveProjectDirectory_ThenReopen_PreservesManuscript()
    {
        var project = await CreateAsync("MoveMe");
        var chapter = await _chapters.CreateAsync(project.Id, "One");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "# One\n\nPortable body.\n");
        var originalRoot = project.RootPath;
        await _projects.CloseAsync();

        var moved = Path.Combine(_tempRoot, "relocated", Path.GetFileName(originalRoot));
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(originalRoot, moved);

        var opened = await _projects.OpenAsync(moved);
        var content = await _chapters.LoadContentAsync(opened.Id, chapter.Id);
        Assert.Equal(project.Id, opened.Id);
        Assert.Equal(moved, opened.RootPath);
        Assert.Contains("Portable body", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorruptedProjectCopy_IsDetectedAndRecoveredSafely()
    {
        var project = await CreateAsync("CorruptCopy");
        var chapter = await _chapters.CreateAsync(project.Id, "Keep");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "safe original");
        var snapshot = await _snapshots.CreateSnapshotAsync(project.Id);
        var root = project.RootPath;
        await _projects.CloseAsync();

        // Deliberately corrupt a *copy* of the database path used for diagnose (do not mutate snapshot).
        var corruptCopyRoot = Path.Combine(_tempRoot, "corrupt-copy");
        CopyDirectory(root, corruptCopyRoot);
        await File.WriteAllBytesAsync(
            Path.Combine(corruptCopyRoot, ProjectPaths.DatabaseFileName),
            Encoding.UTF8.GetBytes("not-a-sqlite-database"));

        Assert.Null(_projects.ActiveProject);
        var report = await _recovery.DiagnoseAsync(corruptCopyRoot);
        Assert.False(report.Integrity.IsHealthy);
        Assert.False(report.DatabaseReadable);
        Assert.DoesNotContain(
            report.DiagnosticNotes,
            note => note.Contains(SecretDraft, StringComparison.Ordinal)
                || note.Contains("safe original", StringComparison.Ordinal));

        // Original project remains openable and intact.
        var opened = await _projects.OpenAsync(root);
        Assert.Equal("safe original", (await _chapters.LoadContentAsync(opened.Id, chapter.Id)).Trim());
        Assert.True(Directory.Exists(snapshot.DirectoryPath));
    }

    [Fact]
    public async Task RestoreCompleteSnapshot_FailureDoesNotHarmCurrentProject()
    {
        var project = await CreateAsync("RestoreSafe");
        var chapter = await _chapters.CreateAsync(project.Id, "KeepMe");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "original content");
        var snapshot = await _snapshots.CreateSnapshotAsync(project.Id);

        await File.WriteAllBytesAsync(
            Path.Combine(snapshot.DirectoryPath, "project.mbws"),
            Encoding.UTF8.GetBytes("not-a-database"));

        var result = await _snapshots.RestoreSnapshotAsync(project.Id, snapshot.DirectoryPath, confirmed: true);
        Assert.False(result.Succeeded);
        Assert.Contains(
            "original content",
            await _chapters.LoadContentAsync(project.Id, chapter.Id),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpgradePreservesSettings_AndInactivePublishingRouteData()
    {
        await _settings.SaveAsync(new ApplicationSettings { SnapshotRetentionLimit = 7 });
        Assert.Equal(7, _settings.GetSettings().SnapshotRetentionLimit);

        var project = await CreateAsync("UpgradeRoutes");
        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.Traditional);
        await _workflow.CompleteStepAsync(project.Id, "20A", stepNumber: 91);
        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.SelfPublishing);
        var applicable = await _workflow.GetApplicablePhasesAsync(project.Id);
        Assert.DoesNotContain(applicable, phase => phase.Id == "20A");

        var root = project.RootPath;
        await _projects.CloseAsync();

        // Simulate app upgrade: new DI graph + reopen (schema migrate path).
        RebuildProvider();
        Assert.Equal(7, _settings.GetSettings().SnapshotRetentionLimit);

        var reopened = await _projects.OpenAsync(root);
        var preserved = await _workflow.GetStepProgressAsync(reopened.Id, "20A", 91);
        Assert.Equal(StepStatus.Complete, preserved.Status);
        await _workflow.SetPublishingRouteAsync(reopened.Id, PublishingRoute.Traditional);
        var traditionalAgain = await _workflow.GetApplicablePhasesAsync(reopened.Id);
        Assert.Contains(traditionalAgain, phase => phase.Id == "20A");
    }

    [Fact]
    public async Task OperatesNormally_WithoutNetworkingDependencies()
    {
        AssertNoForbiddenNetworkTypes(typeof(Core.Domain.Project).Assembly);
        AssertNoForbiddenNetworkTypes(typeof(InfrastructureServiceCollectionExtensions).Assembly);

        var project = await CreateAsync("Offline");
        var chapter = await _chapters.CreateAsync(project.Id, "Alone");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "offline prose");
        var export = await _exports.ExportManuscriptDocxAsync(project.Id, [chapter.Id]);
        Assert.True(File.Exists(export.AbsolutePath));
        Assert.Contains("offline prose", await _chapters.LoadContentAsync(project.Id, chapter.Id), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoHundredOrderedChapters_CompileInSequence()
    {
        var project = await CreateAsync("Compile200");
        var ids = new List<Guid>(200);
        for (var i = 1; i <= 200; i++)
        {
            var chapter = await _chapters.CreateAsync(project.Id, $"Chapter {i:000}");
            ids.Add(chapter.Id);
            await _chapters.SaveContentAsync(project.Id, chapter.Id, $"UNIQUE_MARKER_{i:000}\n");
        }

        var compiled = await _chapters.CompileSelectedAsync(project.Id, ids, "acceptance-200.md");
        Assert.Equal(200, compiled.ChapterCount);
        var text = await File.ReadAllTextAsync(compiled.AbsolutePath);
        var lastIndex = -1;
        for (var i = 1; i <= 200; i++)
        {
            var marker = $"UNIQUE_MARKER_{i:000}";
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(index > lastIndex, $"Marker {marker} out of order.");
            lastIndex = index;
        }
    }

    [Fact]
    public void DiagnosticLogs_NeverIncludeManuscriptContent()
    {
        var dirty = DiagnosticTextSanitizer.Sanitize(
            "Database inspection failed: " + SecretDraft + " " + new string('x', 80));
        Assert.DoesNotContain(SecretDraft, dirty, StringComparison.Ordinal);
        Assert.Contains("redacted", dirty, StringComparison.OrdinalIgnoreCase);

        var longProse = string.Join(' ', Enumerable.Range(0, 60).Select(i => $"word{i}"));
        Assert.True(DiagnosticTextSanitizer.LooksLikeManuscriptPayload(longProse + " " + new string('y', 100)));
        Assert.DoesNotContain("word12", DiagnosticTextSanitizer.Sanitize(longProse + new string('y', 200)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnose_DoesNotEchoJournalDraftIntoNotes()
    {
        var project = await CreateAsync("DiagSafe");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "durable");
        await _journal.UpsertAsync(new RecoveryJournalEntry
        {
            EntryId = RecoveryJournalIds.Create(project.Id, RecoveryEntityType.ManuscriptChapter, chapter.Id),
            ProjectId = project.Id,
            EntityType = RecoveryEntityType.ManuscriptChapter,
            EntityId = chapter.Id,
            DraftText = SecretDraft,
        });
        await _projects.CloseAsync();

        var report = await _recovery.DiagnoseAsync(project.RootPath);
        Assert.NotEmpty(report.JournalEntries);
        Assert.All(report.DiagnosticNotes, note => Assert.DoesNotContain(SecretDraft, note, StringComparison.Ordinal));
        Assert.All(
            report.JournalEntries,
            entry => Assert.False(DiagnosticTextSanitizer.ContainsManuscriptPayload(entry.EntryId.ToString(), SecretDraft)));
    }

    private async Task<Core.Domain.Project> CreateAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);
        return await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title + "-" + Guid.NewGuid().ToString("N")[..8],
            Author = "Acceptance",
            Genre = "Fantasy",
            NorthStar = "v1.1",
        });
    }

    private void RebuildProvider()
    {
        if (_provider is not null)
        {
            _projects.CloseAsync().GetAwaiter().GetResult();
            _provider.Dispose();
            SqliteConnection.ClearAllPools();
        }

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
        _saveState = _provider.GetRequiredService<ISaveStateService>();
        _settings = _provider.GetRequiredService<IApplicationSettingsStore>();
        _workflow = _provider.GetRequiredService<IWorkflowService>();
        _exports = _provider.GetRequiredService<IExportService>();
    }

    private static void AssertNoForbiddenNetworkTypes(Assembly assembly)
    {
        var referenced = assembly.GetReferencedAssemblies().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("System.Net.Http", referenced);

        foreach (var type in assembly.GetTypes())
        {
            Assert.False(typeof(HttpClient).IsAssignableFrom(type), type.FullName);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination), overwrite: true);
        }
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

        throw new DirectoryNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    private sealed class TempPaths(string root) : IApplicationPaths
    {
        public string LocalAppDataDirectory { get; } = root;
    }
}
