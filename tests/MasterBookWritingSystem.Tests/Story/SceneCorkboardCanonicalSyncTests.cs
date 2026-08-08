using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Story;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Story;

public sealed class SceneCorkboardCanonicalSyncTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IStoryDataService _story;
    private readonly IManuscriptHierarchyService _hierarchy;
    private readonly IStoryChangeNotifier _changes;

    public SceneCorkboardCanonicalSyncTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-corkboard-tests", Guid.NewGuid().ToString("N"));
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
        _changes = _provider.GetRequiredService<IStoryChangeNotifier>();
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
    public async Task OneCanonicalScene_IsSharedAcrossSurfaces_AndNotifier()
    {
        var project = await CreateAsync("Canonical Scene");
        var chapter = await _chapters.CreateAsync(project.Id, "Chapter One");
        var observed = new List<StoryChangeEventArgs>();
        _changes.Changed += (_, args) => observed.Add(args);

        var created = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "Opening",
            Location = "Harbor",
            Status = SceneDraftStatus.Outlined,
            WordCount = 120,
        });

        var fromStory = (await _story.GetScenesAsync(project.Id)).Single(item => item.Id == created.Id);
        var tree = await _hierarchy.GetHierarchyAsync(project.Id);
        var fromHierarchy = tree.Books[0].Parts[0].Chapters
            .SelectMany(item => item.Scenes)
            .Single(item => item.Id == created.Id);

        Assert.Equal(created.Id, fromStory.Id);
        Assert.Equal(created.Id, fromHierarchy.Id);
        Assert.Equal("Opening", fromStory.Title);
        Assert.Equal("Opening", fromHierarchy.Title);
        Assert.Equal(120, fromStory.WordCount);

        var chapterPart = new Dictionary<Guid, Guid?> { [chapter.Id] = tree.Books[0].Parts[0].Id };
        var partBook = new Dictionary<Guid, Guid> { [tree.Books[0].Parts[0].Id] = tree.Books[0].Id };
        var chapterSeq = new Dictionary<Guid, int> { [chapter.Id] = chapter.SequenceNumber };
        var corkboardProjection = SceneCorkboardFiltering.Apply(
            await _story.GetScenesAsync(project.Id),
            new SceneCorkboardFilter(),
            chapterPart,
            partBook,
            chapterSeq);
        Assert.Contains(corkboardProjection, item => item.Id == created.Id);

        fromStory.Title = "Opening Revised";
        fromStory.WordCount = 200;
        var updated = await _story.UpdateSceneAsync(project.Id, fromStory);
        var again = (await _story.GetScenesAsync(project.Id)).Single(item => item.Id == created.Id);
        Assert.Equal("Opening Revised", again.Title);
        Assert.Equal(200, again.WordCount);
        Assert.Equal(updated.Id, again.Id);

        Assert.Contains(observed, item =>
            item.ProjectId == project.Id
            && item.Kind == StoryChangeKind.SceneUpserted
            && item.EntityId == created.Id);
        Assert.DoesNotContain(observed, item => item.ProjectId != project.Id);
    }

    [Fact]
    public async Task ReorderViaHierarchy_IsStableAndVisibleToAllReads()
    {
        var project = await CreateAsync("Order Sync");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        var first = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "A",
        });
        var second = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 2,
            Title = "B",
        });

        await _hierarchy.MoveSceneAsync(project.Id, second.Id, direction: -1);

        var ordered = (await _story.GetScenesAsync(project.Id))
            .Where(item => item.ChapterId == chapter.Id)
            .OrderBy(item => item.SequenceNumber)
            .Select(item => item.Id)
            .ToList();
        Assert.Equal([second.Id, first.Id], ordered);

        var tree = await _hierarchy.GetHierarchyAsync(project.Id);
        var hierarchyOrder = tree.Books[0].Parts[0].Chapters
            .Single(item => item.Id == chapter.Id)
            .Scenes
            .Select(item => item.Id)
            .ToList();
        Assert.Equal([second.Id, first.Id], hierarchyOrder);
    }

    [Fact]
    public async Task Notifier_DoesNotLeakAcrossProjects()
    {
        var projectA = await CreateAsync("Project A");
        var chapterA = await _chapters.CreateAsync(projectA.Id, "A1");
        var rootA = projectA.RootPath;

        await _projects.CloseAsync();
        var projectB = await CreateAsync("Project B");
        var chapterB = await _chapters.CreateAsync(projectB.Id, "B1");

        var leaked = new List<StoryChangeEventArgs>();
        _changes.Changed += (_, args) =>
        {
            if (args.ProjectId == projectA.Id)
            {
                leaked.Add(args);
            }
        };

        var sceneB = await _story.CreateSceneAsync(projectB.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = projectB.Id,
            ChapterId = chapterB.Id,
            SequenceNumber = 1,
            Title = "Only B",
        });

        Assert.Empty(leaked);
        Assert.All(await _story.GetScenesAsync(projectB.Id), item => Assert.Equal(projectB.Id, item.ProjectId));
        Assert.Contains(await _story.GetScenesAsync(projectB.Id), item => item.Id == sceneB.Id);

        await _projects.CloseAsync();
        await _projects.OpenAsync(rootA);
        var scenesA = await _story.GetScenesAsync(projectA.Id);
        Assert.DoesNotContain(scenesA, item => item.Id == sceneB.Id);
        Assert.DoesNotContain(scenesA, item => item.Title == "Only B");

        // Ensure project A still has only its own chapter inventory and can own scenes without B's row.
        await _story.CreateSceneAsync(projectA.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = projectA.Id,
            ChapterId = chapterA.Id,
            SequenceNumber = 1,
            Title = "Only A",
        });
        var afterA = await _story.GetScenesAsync(projectA.Id);
        Assert.All(afterA, item => Assert.Equal(projectA.Id, item.ProjectId));
        Assert.DoesNotContain(afterA, item => item.Id == sceneB.Id);
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
