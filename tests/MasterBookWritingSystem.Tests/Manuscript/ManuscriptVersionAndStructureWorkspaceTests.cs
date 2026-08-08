using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Manuscript;

public sealed class ManuscriptVersionAndStructureWorkspaceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IManuscriptVersionService _versions;
    private readonly IChapterStructureService _structure;
    private readonly IStoryDataService _story;

    public ManuscriptVersionAndStructureWorkspaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-version-struct-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _versions = _provider.GetRequiredService<IManuscriptVersionService>();
        _structure = _provider.GetRequiredService<IChapterStructureService>();
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
    public async Task Version_CreateDiffAndRestore_RequiresConfirmation()
    {
        var project = await CreateAsync("Versions");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "before\n");
        var version = await _versions.CreateAsync(project.Id, "Checkpoint", "manual");
        Assert.Equal(1, version.ChapterCount);
        Assert.Contains("ManuscriptVersions", version.AbsolutePath, StringComparison.OrdinalIgnoreCase);

        await _chapters.SaveContentAsync(project.Id, chapter.Id, "after\n");
        var diff = await _versions.DiffAgainstCurrentAsync(project.Id, version.Id);
        Assert.Contains(diff.Lines, line => line.Kind == "before" && line.Text.Contains("before", StringComparison.Ordinal));
        Assert.Contains(diff.Lines, line => line.Kind == "after" && line.Text.Contains("after", StringComparison.Ordinal));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _versions.RestoreAsync(project.Id, version.Id, confirmed: false));
        await _versions.RestoreAsync(project.Id, version.Id, confirmed: true);
        Assert.Equal("before\n", await _chapters.LoadContentAsync(project.Id, chapter.Id));
    }

    [Fact]
    public async Task Split_PreservesMarkersAndRejectsInvalidCut()
    {
        var project = await CreateAsync("Split");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "Right scene",
        });
        var markdown =
            "LEFT PROSE\n\n"
            + SceneProseMarkers.FormatOpen(scene.Id) + "\n"
            + "right scene body\n"
            + SceneProseMarkers.CloseMarker + "\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, markdown);

        var inside = markdown.IndexOf("right scene", StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _structure.SplitChapterAtAsync(project.Id, chapter.Id, inside));
        Assert.Equal(markdown, await _chapters.LoadContentAsync(project.Id, chapter.Id));

        var splitAt = markdown.IndexOf(SceneProseMarkers.FormatOpen(scene.Id), StringComparison.Ordinal);
        var result = await _structure.SplitChapterAtAsync(project.Id, chapter.Id, splitAt, "Right");
        var leftText = await _chapters.LoadContentAsync(project.Id, result.LeftChapter.Id);
        var rightText = await _chapters.LoadContentAsync(project.Id, result.RightChapter.Id);
        Assert.Contains("LEFT PROSE", leftText, StringComparison.Ordinal);
        Assert.DoesNotContain("mbws:scene", leftText, StringComparison.Ordinal);
        Assert.Contains(SceneProseMarkers.FormatOpen(scene.Id), rightText, StringComparison.Ordinal);
        Assert.Equal(result.LeftChapter.SequenceNumber + 1, result.RightChapter.SequenceNumber);

        var moved = (await _story.GetScenesAsync(project.Id)).Single(item => item.Id == scene.Id);
        Assert.Equal(result.RightChapter.Id, moved.ChapterId);
    }

    [Fact]
    public async Task MergeAdjacent_PreservesOrderAndRejectsNonAdjacent()
    {
        var project = await CreateAsync("Merge");
        var first = await _chapters.CreateAsync(project.Id, "One");
        var second = await _chapters.CreateAsync(project.Id, "Two");
        var third = await _chapters.CreateAsync(project.Id, "Three");
        await _chapters.SaveContentAsync(project.Id, first.Id, "AAA\n");
        await _chapters.SaveContentAsync(project.Id, second.Id, "BBB\n");
        await _chapters.SaveContentAsync(project.Id, third.Id, "CCC\n");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _structure.MergeAdjacentChaptersAsync(project.Id, first.Id, third.Id));

        var merged = await _structure.MergeAdjacentChaptersAsync(project.Id, first.Id, second.Id);
        var text = await _chapters.LoadContentAsync(project.Id, merged.SurvivingChapter.Id);
        Assert.Contains("AAA", text, StringComparison.Ordinal);
        Assert.Contains("BBB", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("AAA", StringComparison.Ordinal) < text.IndexOf("BBB", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _chapters.GetAsync(project.Id, second.Id));
    }

    [Fact]
    public async Task InjectedChapterFileFailure_DuringSplit_RestoresOriginalChapter()
    {
        var project = await CreateAsync("SplitFail");
        var chapter = await _chapters.CreateAsync(project.Id, "Host");
        const string original = "LEFT SIDE\n\nRIGHT SIDE\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, original);
        var root = project.RootPath;
        var chapterId = chapter.Id;
        await _projects.CloseAsync();

        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        var chapterFileStoreDescriptor = services.First(item => item.ServiceType == typeof(IChapterFileStore));
        services.Remove(chapterFileStoreDescriptor);
        // Fail only the first write that is exactly the right-hand split body so compensation can restore.
        services.AddSingleton<IChapterFileStore>(_ => new FailOnceOnExactContentChapterFileStore(
            new ChapterFileStore(),
            failWhenEquals: "RIGHT SIDE\n"));
        await using var provider = services.BuildServiceProvider();
        var projects = provider.GetRequiredService<IProjectService>();
        var chapters = provider.GetRequiredService<IChapterService>();
        var structure = provider.GetRequiredService<IChapterStructureService>();
        await projects.OpenAsync(root);

        var splitAt = original.IndexOf("RIGHT SIDE", StringComparison.Ordinal);
        await Assert.ThrowsAsync<IOException>(() =>
            structure.SplitChapterAtAsync(project.Id, chapterId, splitAt, "Right"));

        Assert.Equal(original, await chapters.LoadContentAsync(project.Id, chapterId));
        Assert.Single(await chapters.GetAllAsync(project.Id));
        await projects.CloseAsync();
    }

    [Fact]
    public async Task InjectedChapterFileFailure_DuringReplace_CompensatesPriorChapter()
    {
        var project = await CreateAsync("FailStore");
        var first = await _chapters.CreateAsync(project.Id, "One");
        var second = await _chapters.CreateAsync(project.Id, "Two");
        await _chapters.SaveContentAsync(project.Id, first.Id, "token-a\n");
        await _chapters.SaveContentAsync(project.Id, second.Id, "token-b\n");
        var root = project.RootPath;
        await _projects.CloseAsync();

        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        var chapterFileStoreDescriptor = services.First(item => item.ServiceType == typeof(IChapterFileStore));
        services.Remove(chapterFileStoreDescriptor);
        services.AddSingleton<IChapterFileStore>(_ => new FailOnContentChapterFileStore(
            new ChapterFileStore(),
            failWhenContains: "TOKEN-b"));
        await using var provider = services.BuildServiceProvider();
        var projects = provider.GetRequiredService<IProjectService>();
        var chapters = provider.GetRequiredService<IChapterService>();
        var search = provider.GetRequiredService<IManuscriptSearchReplaceService>();
        await projects.OpenAsync(root);

        var options = new ManuscriptSearchOptions { FindText = "token", ReplacementText = "TOKEN" };
        var preview = await search.PreviewAsync(project.Id, options);
        await Assert.ThrowsAsync<IOException>(() =>
            search.ApplyAsync(project.Id, options, preview.Hits, confirmed: true));

        Assert.Equal("token-a\n", await chapters.LoadContentAsync(project.Id, first.Id));
        Assert.Equal("token-b\n", await chapters.LoadContentAsync(project.Id, second.Id));
        await projects.CloseAsync();
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
            NorthStar = "Structure",
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

    private sealed class FailOnContentChapterFileStore(IChapterFileStore inner, string failWhenContains) : IChapterFileStore
    {
        public Task SaveAsync(
            string projectRootPath,
            Core.Domain.Manuscript.Chapter chapter,
            string markdownContent,
            CancellationToken cancellationToken = default)
        {
            if (markdownContent.Contains(failWhenContains, StringComparison.Ordinal))
            {
                throw new IOException("Injected chapter file save failure.");
            }

            return inner.SaveAsync(projectRootPath, chapter, markdownContent, cancellationToken);
        }

        public Task<string> LoadAsync(
            string projectRootPath,
            Core.Domain.Manuscript.Chapter chapter,
            CancellationToken cancellationToken = default)
            => inner.LoadAsync(projectRootPath, chapter, cancellationToken);
    }

    private sealed class FailOnceOnExactContentChapterFileStore(IChapterFileStore inner, string failWhenEquals) : IChapterFileStore
    {
        private int _failures;

        public Task SaveAsync(
            string projectRootPath,
            Core.Domain.Manuscript.Chapter chapter,
            string markdownContent,
            CancellationToken cancellationToken = default)
        {
            if (_failures == 0
                && string.Equals(markdownContent, failWhenEquals, StringComparison.Ordinal))
            {
                _failures++;
                throw new IOException("Injected one-shot chapter file save failure.");
            }

            return inner.SaveAsync(projectRootPath, chapter, markdownContent, cancellationToken);
        }

        public Task<string> LoadAsync(
            string projectRootPath,
            Core.Domain.Manuscript.Chapter chapter,
            CancellationToken cancellationToken = default)
            => inner.LoadAsync(projectRootPath, chapter, cancellationToken);
    }
}
