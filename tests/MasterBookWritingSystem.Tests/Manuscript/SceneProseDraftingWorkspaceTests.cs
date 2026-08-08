using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Manuscript;

public sealed class SceneProseDraftingWorkspaceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IStoryDataService _story;
    private readonly IManuscriptHierarchyService _hierarchy;
    private readonly IRecoveryJournalService _journal;
    private readonly IEditorAutosaveService _autosave;

    public SceneProseDraftingWorkspaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-scene-prose-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _story = _provider.GetRequiredService<IStoryDataService>();
        _hierarchy = _provider.GetRequiredService<IManuscriptHierarchyService>();
        _journal = _provider.GetRequiredService<IRecoveryJournalService>();
        _autosave = _provider.GetRequiredService<IEditorAutosaveService>();
    }

    public void Dispose()
    {
        _projects.CloseAsync().GetAwaiter().GetResult();
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task LegacyChapter_OpensAndSavesUnchanged_WithoutInferredMarkers()
    {
        var project = await CreateAsync("Legacy");
        var chapter = await _chapters.CreateAsync(project.Id, "One");
        const string body = "# One\n\nUnstructured chapter body stays put.\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, body);

        var loaded = await _chapters.LoadContentAsync(project.Id, chapter.Id);
        Assert.Equal(body, loaded);
        Assert.DoesNotContain("mbws:scene", loaded, StringComparison.Ordinal);

        await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "Outline only",
        });

        var again = await _chapters.LoadContentAsync(project.Id, chapter.Id);
        Assert.Equal(body, again);
    }

    [Fact]
    public async Task AssignScene_AppendsEmptyRegion_FileFirstThenDb()
    {
        var project = await CreateAsync("Assign");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "# Ch\n\nLead-in prose.\n");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 1,
            Title = "Beat",
        });

        await _hierarchy.AssignSceneToChapterAsync(project.Id, scene.Id, chapter.Id);
        var markdown = await _chapters.LoadContentAsync(project.Id, chapter.Id);
        Assert.Contains(SceneProseMarkers.FormatOpen(scene.Id), markdown, StringComparison.Ordinal);
        Assert.Contains(SceneProseMarkers.CloseMarker, markdown, StringComparison.Ordinal);
        Assert.Contains("Lead-in prose.", markdown, StringComparison.Ordinal);

        var entryId = RecoveryJournalIds.Create(project.Id, RecoveryEntityType.ManuscriptChapter, chapter.Id);
        Assert.Null(await _journal.GetAsync(project.Id, entryId));
    }

    [Fact]
    public async Task SaveContent_UpdatesSceneWordCounts_FromAssociations()
    {
        var project = await CreateAsync("Counts");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "S",
        });

        var markdown =
            SceneProseMarkers.FormatOpen(scene.Id) + "\n"
            + "one two three four\n"
            + SceneProseMarkers.CloseMarker + "\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, markdown);

        var updated = (await _story.GetScenesAsync(project.Id)).Single(item => item.Id == scene.Id);
        Assert.Equal(4, updated.WordCount);
        var chapterRow = await _chapters.GetAsync(project.Id, chapter.Id);
        Assert.Equal(4, chapterRow.WordCount);
    }

    [Fact]
    public async Task Compile_StripsInternalSceneMarkers()
    {
        var project = await CreateAsync("Compile");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        var sceneId = Guid.NewGuid();
        var markdown =
            "Visible.\n"
            + SceneProseMarkers.FormatOpen(sceneId) + "\n"
            + "Inside scene.\n"
            + SceneProseMarkers.CloseMarker + "\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, markdown);

        var result = await _chapters.CompileSelectedAsync(
            project.Id,
            [chapter.Id],
            "compile-markers.md");
        var exported = await File.ReadAllTextAsync(result.AbsolutePath);
        Assert.DoesNotContain("mbws:scene", exported, StringComparison.Ordinal);
        Assert.Contains("Visible.", exported, StringComparison.Ordinal);
        Assert.Contains("Inside scene.", exported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteScene_UnwrapsMarkers_KeepsProse()
    {
        var project = await CreateAsync("DeleteUnwrap");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "S",
        });
        var markdown = SceneProseAssociation.AssociateSelection("Keep my words", scene.Id, 0, "Keep my words".Length);
        await _chapters.SaveContentAsync(project.Id, chapter.Id, markdown);

        await _story.DeleteSceneAsync(project.Id, scene.Id);
        var after = await _chapters.LoadContentAsync(project.Id, chapter.Id);
        Assert.DoesNotContain("mbws:scene", after, StringComparison.Ordinal);
        Assert.Contains("Keep my words", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Autosave_DoesNotOverwriteNewerEdits_WithStaleDraft()
    {
        var project = await CreateAsync("Race");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "v1\n");

        _autosave.ScheduleManuscriptSave(project.Id, chapter.Id, "stale-draft\n");
        _autosave.ScheduleManuscriptSave(project.Id, chapter.Id, "newest-draft\n");
        await _autosave.FlushAsync();

        var loaded = await _chapters.LoadContentAsync(project.Id, chapter.Id);
        Assert.Equal("newest-draft\n", loaded);
    }

    [Fact]
    public async Task FailedSave_RetainsCrashJournal_WithLatestDraft()
    {
        var project = await CreateAsync("Journal");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        var entryId = RecoveryJournalIds.Create(project.Id, RecoveryEntityType.ManuscriptChapter, chapter.Id);
        await _journal.UpsertAsync(new RecoveryJournalEntry
        {
            EntryId = entryId,
            ProjectId = project.Id,
            EntityType = RecoveryEntityType.ManuscriptChapter,
            EntityId = chapter.Id,
            DraftText = "crash draft with " + SceneProseMarkers.FormatOpen(Guid.NewGuid()),
        });

        var retained = await _journal.GetAsync(project.Id, entryId);
        Assert.NotNull(retained);
        Assert.Contains("mbws:scene", retained!.DraftText, StringComparison.Ordinal);
    }

    private async Task<Project> CreateAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        return await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title,
            Author = "Tester",
            Genre = "Fantasy",
            NorthStar = "Draft",
        });
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
}
