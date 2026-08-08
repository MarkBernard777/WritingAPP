using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Manuscript;

public sealed class ManuscriptSearchReplaceWorkspaceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IManuscriptHierarchyService _hierarchy;
    private readonly IManuscriptSearchReplaceService _search;
    private readonly IStoryDataService _story;

    public ManuscriptSearchReplaceWorkspaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-search-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _hierarchy = _provider.GetRequiredService<IManuscriptHierarchyService>();
        _search = _provider.GetRequiredService<IManuscriptSearchReplaceService>();
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
    public async Task Preview_ScopesBySelectedChapters()
    {
        var project = await CreateAsync("Scope");
        var a = await _chapters.CreateAsync(project.Id, "A");
        var b = await _chapters.CreateAsync(project.Id, "B");
        await _chapters.SaveContentAsync(project.Id, a.Id, "find me in A\n");
        await _chapters.SaveContentAsync(project.Id, b.Id, "find me in B\n");

        var preview = await _search.PreviewAsync(
            project.Id,
            new ManuscriptSearchOptions
            {
                FindText = "find me",
                ScopeKind = ManuscriptSearchScopeKind.SelectedChapters,
                SelectedChapterIds = [a.Id],
            });

        Assert.Equal(1, preview.ChapterCountScanned);
        Assert.All(preview.Hits, hit => Assert.Equal(a.Id, hit.ChapterId));
    }

    [Fact]
    public async Task Preview_ScopesByBookAndPart()
    {
        var project = await CreateAsync("BookScope");
        // Create chapters under the initial default part before adding another book/part.
        var inDefault = await _chapters.CreateAsync(project.Id, "Default");
        var inOther = await _chapters.CreateAsync(project.Id, "Other");
        await _chapters.SaveContentAsync(project.Id, inDefault.Id, "find me default\n");
        await _chapters.SaveContentAsync(project.Id, inOther.Id, "find me other\n");

        var bookA = (await _hierarchy.GetHierarchyAsync(project.Id)).Books[0];
        var bookB = await _hierarchy.CreateBookAsync(project.Id, "Other Book");
        var partB = await _hierarchy.CreatePartAsync(project.Id, bookB.Id, "Other Part");
        await _hierarchy.MoveChapterToPartAsync(project.Id, inOther.Id, partB.Id);

        var bookPreview = await _search.PreviewAsync(
            project.Id,
            new ManuscriptSearchOptions
            {
                FindText = "find me",
                ScopeKind = ManuscriptSearchScopeKind.Book,
                BookId = bookA.Id,
            });
        Assert.Single(bookPreview.Hits);
        Assert.Equal(inDefault.Id, bookPreview.Hits[0].ChapterId);

        var partPreview = await _search.PreviewAsync(
            project.Id,
            new ManuscriptSearchOptions
            {
                FindText = "find me",
                ScopeKind = ManuscriptSearchScopeKind.Part,
                PartId = partB.Id,
            });
        Assert.Single(partPreview.Hits);
        Assert.Equal(inOther.Id, partPreview.Hits[0].ChapterId);

        var entire = await _search.PreviewAsync(
            project.Id,
            new ManuscriptSearchOptions
            {
                FindText = "find me",
                ScopeKind = ManuscriptSearchScopeKind.EntireManuscript,
            });
        Assert.Equal(2, entire.Hits.Count);
    }

    [Fact]
    public async Task Apply_RequiresConfirmation_AndSupportsRollback()
    {
        var project = await CreateAsync("Replace");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "alpha beta alpha\n");
        var options = new ManuscriptSearchOptions
        {
            FindText = "alpha",
            ReplacementText = "ALPHA",
        };
        var preview = await _search.PreviewAsync(project.Id, options);
        Assert.Equal(2, preview.Hits.Count);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _search.ApplyAsync(project.Id, options, preview.Hits, confirmed: false));

        preview.Hits[1].Include = false;
        var result = await _search.ApplyAsync(project.Id, options, preview.Hits, confirmed: true);
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.ReplacementCount);
        Assert.NotNull(result.RollbackToken);
        Assert.Equal("ALPHA beta alpha\n", await _chapters.LoadContentAsync(project.Id, chapter.Id));

        await _search.RollbackAsync(project.Id, result.RollbackToken!.Value, confirmed: true);
        Assert.Equal("alpha beta alpha\n", await _chapters.LoadContentAsync(project.Id, chapter.Id));
    }

    [Fact]
    public async Task Apply_Cancellation_CompensatesPartialWrites()
    {
        var project = await CreateAsync("Cancel");
        var first = await _chapters.CreateAsync(project.Id, "One");
        var second = await _chapters.CreateAsync(project.Id, "Two");
        await _chapters.SaveContentAsync(project.Id, first.Id, "token one\n");
        await _chapters.SaveContentAsync(project.Id, second.Id, "token two\n");

        var options = new ManuscriptSearchOptions
        {
            FindText = "token",
            ReplacementText = "TOKEN",
        };
        var preview = await _search.PreviewAsync(project.Id, options);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _search.ApplyAsync(project.Id, options, preview.Hits, confirmed: true, cts.Token));

        Assert.Equal("token one\n", await _chapters.LoadContentAsync(project.Id, first.Id));
        Assert.Equal("token two\n", await _chapters.LoadContentAsync(project.Id, second.Id));
    }

    [Fact]
    public async Task MarkerIntegrity_ReplaceDoesNotCorruptSceneDelimitersWhenOutside()
    {
        var project = await CreateAsync("Markers");
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
            "Intro word.\n"
            + SceneProseMarkers.FormatOpen(scene.Id) + "\n"
            + "scene body\n"
            + SceneProseMarkers.CloseMarker + "\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, markdown);
        var options = new ManuscriptSearchOptions { FindText = "word", ReplacementText = "WORD" };
        var preview = await _search.PreviewAsync(project.Id, options);
        await _search.ApplyAsync(project.Id, options, preview.Hits, confirmed: true);
        var after = await _chapters.LoadContentAsync(project.Id, chapter.Id);
        Assert.Contains(SceneProseMarkers.FormatOpen(scene.Id), after, StringComparison.Ordinal);
        Assert.Contains(SceneProseMarkers.CloseMarker, after, StringComparison.Ordinal);
        Assert.Contains("Intro WORD.", after, StringComparison.Ordinal);
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
            NorthStar = "Search",
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
