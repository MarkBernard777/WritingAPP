using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Story;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Story;

public sealed class SceneChapterUsabilityTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IStoryDataService _story;

    public SceneChapterUsabilityTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-scene-chapter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _story = _provider.GetRequiredService<IStoryDataService>();
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
    public async Task MultipleScenes_CanShareOneChapter_AndRemainCanonicalAcrossUpdates()
    {
        var project = await CreateAsync("Shared");
        var chapter = await _chapters.CreateAsync(project.Id, "Arrival");
        var first = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 1,
            Title = "Dock",
            ChapterId = chapter.Id,
        });
        var second = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 2,
            Title = "Market",
            ChapterId = chapter.Id,
        });

        first.Title = "Dockside";
        await _story.UpdateSceneAsync(project.Id, first);

        var loaded = await _story.GetScenesAsync(project.Id);
        Assert.Equal(2, loaded.Count(item => item.ChapterId == chapter.Id));
        Assert.Equal("Dockside", (await _story.GetSceneAsync(project.Id, first.Id)).Title);
        Assert.Equal(chapter.Id, (await _story.GetSceneAsync(project.Id, second.Id)).ChapterId);
    }

    [Fact]
    public async Task Scenes_DistributeAcrossChapters_AndFilterAndOrder()
    {
        var project = await CreateAsync("Distribute");
        var chapterA = await _chapters.CreateAsync(project.Id, "Alpha");
        var chapterB = await _chapters.CreateAsync(project.Id, "Beta");
        // Scene sequence numbers are unique project-wide; ordering still groups by chapter then sequence.
        await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 3,
            Title = "Late in Alpha",
            ChapterId = chapterA.Id,
        });
        await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 2,
            Title = "Early in Beta",
            ChapterId = chapterB.Id,
        });
        await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 1,
            Title = "Early in Alpha",
            ChapterId = chapterA.Id,
        });
        await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 9,
            Title = "Loose",
            ChapterId = null,
        });

        var chapters = await _chapters.GetAllAsync(project.Id);
        var sequences = chapters.ToDictionary(item => item.Id, item => item.SequenceNumber);
        var all = await _story.GetScenesAsync(project.Id);
        var ordered = SceneChapterOrdering.OrderByChapterThenSequence(all, sequences);
        Assert.Equal(
            ["Early in Alpha", "Late in Alpha", "Early in Beta", "Loose"],
            ordered.Select(item => item.Title));

        var alphaOnly = SceneChapterOrdering.FilterByChapter(all, chapterA.Id, unassignedOnly: false);
        Assert.Equal(2, alphaOnly.Count);
        Assert.All(alphaOnly, item => Assert.Equal(chapterA.Id, item.ChapterId));

        var unassigned = SceneChapterOrdering.FilterByChapter(all, null, unassignedOnly: true);
        Assert.Single(unassigned);
        Assert.Equal("Loose", unassigned[0].Title);
    }

    [Fact]
    public async Task DeleteChapter_UnassignsScenes_DoesNotDeleteThem()
    {
        var project = await CreateAsync("Unassign");
        var chapter = await _chapters.CreateAsync(project.Id, "Doomed");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 1,
            Title = "Survives",
            ChapterId = chapter.Id,
        });

        await _chapters.DeleteAsync(project.Id, chapter.Id);
        var loaded = await _story.GetSceneAsync(project.Id, scene.Id);
        Assert.Null(loaded.ChapterId);
        Assert.Equal("Survives", loaded.Title);
        Assert.Single(await _story.GetScenesAsync(project.Id));
    }

    private async Task<Project> CreateAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library");
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

        throw new FileNotFoundException(string.Join('/', parts));
    }
}
