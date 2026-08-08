using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Recovery;

public sealed class CrashJournalAndAutosaveTests : IDisposable
{
    private readonly string _tempRoot;
    private ServiceProvider _provider = null!;
    private IProjectService _projects = null!;
    private IChapterService _chapters = null!;
    private IDocumentService _documents = null!;
    private IRecoveryJournalService _journal = null!;
    private IEditorAutosaveService _autosave = null!;
    private ISaveStateService _saveState = null!;

    public CrashJournalAndAutosaveTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-recovery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _provider = BuildProvider();
        ResolveServices();
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
    public async Task Debounce_WaitsThenSaves_LatestEditWins()
    {
        var project = await CreateAsync("Debounce");
        var chapter = await _chapters.CreateAsync(project.Id, "One");
        _autosave.DebounceDelay = TimeSpan.FromMilliseconds(80);

        _autosave.ScheduleManuscriptSave(project.Id, chapter.Id, "draft-1");
        await Task.Delay(20);
        _autosave.ScheduleManuscriptSave(project.Id, chapter.Id, "draft-2");
        await Task.Delay(20);
        _autosave.ScheduleManuscriptSave(project.Id, chapter.Id, "draft-final");

        await WaitForAsync(() => !_autosave.HasPendingWork, TimeSpan.FromSeconds(3));

        var loaded = await _chapters.LoadContentAsync(project.Id, chapter.Id);
        Assert.Equal("draft-final", loaded);
        Assert.False(await _journal.HasRecoverableEntriesAsync(project.Id));
    }

    [Fact]
    public async Task Journal_WrittenBeforeSave_ClearedAfterSuccess()
    {
        var project = await CreateAsync("JournalSuccess");
        var chapter = await _chapters.CreateAsync(project.Id, "Chapter");
        var entryId = RecoveryJournalIds.Create(
            project.Id,
            RecoveryEntityType.ManuscriptChapter,
            chapter.Id);

        await _chapters.SaveContentAsync(project.Id, chapter.Id, "durable body");
        Assert.Null(await _journal.GetAsync(project.Id, entryId));
        Assert.Equal("durable body", await _chapters.LoadContentAsync(project.Id, chapter.Id));
    }

    [Fact]
    public async Task Journal_RetainedWhenDurableSaveFails()
    {
        var project = await CreateAsync("JournalFail");
        var chapter = await _chapters.CreateAsync(project.Id, "Chapter");
        var root = project.RootPath;
        var projectId = project.Id;
        var chapterId = chapter.Id;
        var entryId = RecoveryJournalIds.Create(
            projectId,
            RecoveryEntityType.ManuscriptChapter,
            chapterId);

        RebuildProvider(services =>
        {
            services.AddSingleton<IChapterFileStore, ThrowingChapterFileStore>();
        });
        await _projects.OpenAsync(root);

        await Assert.ThrowsAsync<IOException>(() =>
            _chapters.SaveContentAsync(projectId, chapterId, "unsaved crash draft"));

        var retained = await _journal.GetAsync(projectId, entryId);
        Assert.NotNull(retained);
        Assert.Equal("unsaved crash draft", retained!.DraftText);
        Assert.True(await _journal.HasRecoverableEntriesAsync(projectId));
    }

    [Fact]
    public async Task Flush_CancelsDebounceAndPersistsImmediately()
    {
        var project = await CreateAsync("Flush");
        var chapter = await _chapters.CreateAsync(project.Id, "Chapter");
        _autosave.DebounceDelay = TimeSpan.FromSeconds(30);
        _autosave.ScheduleManuscriptSave(project.Id, chapter.Id, "flushed-now");
        Assert.True(_autosave.HasPendingWork);

        await _autosave.FlushAsync();
        Assert.False(_autosave.HasPendingWork);
        Assert.Equal("flushed-now", await _chapters.LoadContentAsync(project.Id, chapter.Id));
    }

    [Fact]
    public async Task CancelAll_PreventsPendingDebouncedSave()
    {
        var project = await CreateAsync("Cancel");
        var chapter = await _chapters.CreateAsync(project.Id, "Chapter");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "original");
        _autosave.DebounceDelay = TimeSpan.FromMilliseconds(200);
        _autosave.ScheduleManuscriptSave(project.Id, chapter.Id, "should-not-save");
        _autosave.CancelAll();
        await Task.Delay(350);
        Assert.Equal("original", await _chapters.LoadContentAsync(project.Id, chapter.Id));
    }

    [Fact]
    public async Task ProjectIsolation_JournalBelongsToActiveProjectOnly()
    {
        var first = await CreateAsync("IsoA");
        var chapter = await _chapters.CreateAsync(first.Id, "A");
        await _journal.UpsertAsync(new RecoveryJournalEntry
        {
            EntryId = RecoveryJournalIds.Create(first.Id, RecoveryEntityType.ManuscriptChapter, chapter.Id),
            ProjectId = first.Id,
            EntityType = RecoveryEntityType.ManuscriptChapter,
            EntityId = chapter.Id,
            DraftText = "a-draft",
        });
        Assert.True(await _journal.HasRecoverableEntriesAsync(first.Id));

        await _projects.CloseAsync();
        var second = await CreateAsync("IsoB");
        Assert.False(await _journal.HasRecoverableEntriesAsync(second.Id));
        Assert.Empty(await _journal.ListAsync(second.Id));
    }

    [Fact]
    public async Task DocumentFieldAndNotes_JournalLifecycle()
    {
        var project = await CreateAsync("Docs");
        var docs = await _documents.GetAllAsync(project.Id);
        var document = docs[0];
        var field = document.Fields[0];

        await _documents.UpdateFieldAsync(project.Id, document.Id, field.Key, "field-value");
        await _documents.UpdateNotesAsync(project.Id, document.Id, "notes-value");

        Assert.False(await _journal.HasRecoverableEntriesAsync(project.Id));
        var reloaded = await _documents.GetAsync(project.Id, document.DocumentType);
        Assert.Equal("field-value", reloaded.Fields.First(item => item.Key == field.Key).Value);
        Assert.Equal("notes-value", reloaded.Notes);
    }

    [Fact]
    public async Task UndoNewestState_DoesNotResurrectClearedJournal()
    {
        var project = await CreateAsync("UndoJournal");
        var chapter = await _chapters.CreateAsync(project.Id, "Chapter");
        var entryId = RecoveryJournalIds.Create(
            project.Id,
            RecoveryEntityType.ManuscriptChapter,
            chapter.Id);

        _autosave.DebounceDelay = TimeSpan.FromMilliseconds(40);
        _autosave.ScheduleManuscriptSave(project.Id, chapter.Id, "edited");
        await WaitForAsync(() => !_autosave.HasPendingWork, TimeSpan.FromSeconds(3));
        Assert.Equal("edited", await _chapters.LoadContentAsync(project.Id, chapter.Id));
        Assert.Null(await _journal.GetAsync(project.Id, entryId));

        // Simulate undo restoring prior prose: newest draft is autosaved; cleared journal stays cleared after success.
        _autosave.ScheduleManuscriptSave(project.Id, chapter.Id, "original");
        await WaitForAsync(() => !_autosave.HasPendingWork, TimeSpan.FromSeconds(3));

        Assert.Equal("original", await _chapters.LoadContentAsync(project.Id, chapter.Id));
        Assert.Null(await _journal.GetAsync(project.Id, entryId));
        Assert.False(await _journal.HasRecoverableEntriesAsync(project.Id));
    }

    [Fact]
    public async Task ListAsync_ExposesInspectionApiForRecoverySlice()
    {
        var project = await CreateAsync("Inspect");
        var chapter = await _chapters.CreateAsync(project.Id, "Chapter");
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
            DraftText = "inspect-me",
        });

        var listed = await _journal.ListAsync(project.Id);
        Assert.Contains(listed, item => item.EntryId == entryId && item.DraftLength == "inspect-me".Length);
        var loaded = await _journal.GetAsync(project.Id, entryId);
        Assert.Equal("inspect-me", loaded!.DraftText);
        Assert.True(Path.IsPathRooted(listed[0].AbsolutePath));
        Assert.Contains("13 Archive", listed[0].AbsolutePath.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecoveryJournalIds_AreStable()
    {
        var projectId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var first = RecoveryJournalIds.Create(projectId, RecoveryEntityType.DocumentField, entityId, "north_star");
        var second = RecoveryJournalIds.Create(projectId, RecoveryEntityType.DocumentField, entityId, "north_star");
        Assert.Equal(first, second);
        Assert.NotEqual(
            first,
            RecoveryJournalIds.Create(projectId, RecoveryEntityType.DocumentNotes, entityId));
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

    private void RebuildProvider(Action<IServiceCollection>? configure = null)
    {
        _projects.CloseAsync().GetAwaiter().GetResult();
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        _provider = BuildProvider(configure);
        ResolveServices();
    }

    private ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPaths>(new TempApplicationPaths(Path.Combine(_tempRoot, "appdata")));
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private void ResolveServices()
    {
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _documents = _provider.GetRequiredService<IDocumentService>();
        _journal = _provider.GetRequiredService<IRecoveryJournalService>();
        _autosave = _provider.GetRequiredService<IEditorAutosaveService>();
        _saveState = _provider.GetRequiredService<ISaveStateService>();
        _autosave.DebounceDelay = TimeSpan.FromMilliseconds(40);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(condition(), "Timed out waiting for condition.");
    }

    private sealed class TempApplicationPaths(string root) : IApplicationPaths
    {
        public string LocalAppDataDirectory { get; } = root;
    }

    private sealed class ThrowingChapterFileStore : IChapterFileStore
    {
        public Task SaveAsync(
            string projectRootPath,
            Chapter chapter,
            string markdownContent,
            CancellationToken cancellationToken = default)
            => throw new IOException("Simulated durable save failure.");

        public Task<string> LoadAsync(
            string projectRootPath,
            Chapter chapter,
            CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
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
