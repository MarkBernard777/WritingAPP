using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Hierarchy;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Hierarchy;

/// <summary>
/// Headless workspace behaviours for the Manuscript cockpit (selection, moves, refresh stability).
/// </summary>
public sealed class ManuscriptHierarchyWorkspaceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IStoryDataService _story;
    private readonly IManuscriptHierarchyService _hierarchy;

    public ManuscriptHierarchyWorkspaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-hierarchy-workspace", Guid.NewGuid().ToString("N"));
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
    public async Task Selection_SceneResolvesParentChapterWithoutDuplicatingScene()
    {
        var project = await CreateAsync("Select");
        var chapter = await _chapters.CreateAsync(project.Id, "Host");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "One",
        });

        var selection = ManuscriptHierarchyInteractions.ResolveSelection(
            HierarchyNodeKind.Scene,
            scene.Id,
            chapter.Id);

        Assert.Equal(chapter.Id, selection.EditorChapterId);
        Assert.Equal(scene.Id, selection.ContextSceneId);

        var tree = await _hierarchy.GetHierarchyAsync(project.Id);
        var scenes = tree.Books.SelectMany(b => b.Parts)
            .SelectMany(p => p.Chapters)
            .SelectMany(c => c.Scenes)
            .Where(s => s.Id == scene.Id)
            .ToList();
        Assert.Single(scenes);
    }

    [Fact]
    public async Task MoveCommands_PersistOrderAcrossReopen()
    {
        var project = await CreateAsync("Persist");
        var bookB = await _hierarchy.CreateBookAsync(project.Id, "B");
        await _hierarchy.MoveBookAsync(project.Id, bookB.Id, direction: -1);
        var c1 = await _chapters.CreateAsync(project.Id, "One");
        var c2 = await _chapters.CreateAsync(project.Id, "Two");
        await _chapters.MoveUpAsync(project.Id, c2.Id);

        var root = project.RootPath;
        await _projects.CloseAsync();
        var reopened = await _projects.OpenAsync(root);
        var tree = await _hierarchy.GetHierarchyAsync(reopened.Id);

        Assert.Equal(["B", "Persist"], tree.Books.Select(b => b.Title));
        Assert.Equal([1, 2], tree.Books.Select(b => b.SequenceNumber));
        var chapters = await _chapters.GetAllAsync(reopened.Id);
        Assert.Equal(["Two", "One"], chapters.Select(c => c.Title));
        Assert.Equal(c2.Id, chapters[0].Id);
        Assert.Equal(c1.Id, chapters[1].Id);
    }

    [Fact]
    public async Task InvalidDrop_DoesNotChangeHierarchy()
    {
        var project = await CreateAsync("InvalidDrop");
        var treeBefore = await _hierarchy.GetHierarchyAsync(project.Id);
        var bookId = treeBefore.Books[0].Id;
        var partId = treeBefore.Books[0].Parts[0].Id;
        var chapter = await _chapters.CreateAsync(project.Id, "Alpha");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "S",
        });

        var action = ManuscriptHierarchyInteractions.ClassifyDrop(
            HierarchyNodeKind.Scene,
            scene.Id,
            HierarchyNodeKind.Book,
            bookId,
            chapter.Id,
            null);
        Assert.Equal(HierarchyDropAction.None, action);

        var treeAfter = await _hierarchy.GetHierarchyAsync(project.Id);
        Assert.Equal(partId, treeAfter.Books[0].Parts[0].Id);
        Assert.Equal(chapter.Id, treeAfter.Books[0].Parts[0].Chapters[0].Id);
        Assert.Equal(scene.Id, treeAfter.Books[0].Parts[0].Chapters[0].Scenes[0].Id);
    }

    [Fact]
    public async Task RefreshStability_PreservesExpansionKeysAndSelectionTarget()
    {
        var project = await CreateAsync("Refresh");
        var tree = await _hierarchy.GetHierarchyAsync(project.Id);
        var bookId = tree.Books[0].Id;
        var partId = tree.Books[0].Parts[0].Id;
        var chapter = await _chapters.CreateAsync(project.Id, "Keep");

        var expanded = ManuscriptHierarchyInteractions.CaptureExpanded(
        [
            (HierarchyNodeKind.Book, bookId, true),
            (HierarchyNodeKind.Part, partId, true),
            (HierarchyNodeKind.Chapter, chapter.Id, false),
        ]);

        Assert.True(ManuscriptHierarchyInteractions.ShouldExpand(
            HierarchyNodeKind.Book, bookId, expanded, false));
        Assert.False(ManuscriptHierarchyInteractions.ShouldExpand(
            HierarchyNodeKind.Chapter, chapter.Id, expanded, true));

        var selection = ManuscriptHierarchyInteractions.ResolveSelection(
            HierarchyNodeKind.Chapter,
            chapter.Id,
            null);
        Assert.Equal(chapter.Id, selection.EditorChapterId);
    }

    [Fact]
    public async Task ProjectSwitch_RejectsCrossProjectHierarchyMutation()
    {
        var first = await CreateAsync("First");
        var secondParent = Path.Combine(_tempRoot, "lib2");
        Directory.CreateDirectory(secondParent);
        var second = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = secondParent,
            Title = "Second",
            Author = "T",
            Genre = "F",
            NorthStar = "N",
        });

        var foreignBook = (await _hierarchy.GetHierarchyAsync(second.Id)).Books[0].Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _hierarchy.CreatePartAsync(first.Id, foreignBook, "Nope"));
    }

    [Fact]
    public async Task Cancellation_StopsHierarchyLoad()
    {
        var project = await CreateAsync("Cancel");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _hierarchy.GetHierarchyAsync(project.Id, cts.Token));
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
            NorthStar = "Finish",
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
