using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Manuscript;

/// <summary>
/// First-pass manual checklist for split / merge / replace / restore on disposable project copies
/// (never against a user library project).
/// </summary>
public sealed class DisposableProjectManualFlowTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IManuscriptSearchReplaceService _search;
    private readonly IManuscriptVersionService _versions;
    private readonly IChapterStructureService _structure;
    private readonly IStoryDataService _story;

    public DisposableProjectManualFlowTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-manual-disposable", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _search = _provider.GetRequiredService<IManuscriptSearchReplaceService>();
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
    public async Task DisposableCopy_SplitMergeReplaceRestore()
    {
        var project = await CreateDisposableAsync("ManualFlow");
        var chapter = await _chapters.CreateAsync(project.Id, "Host");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "Tail",
        });
        var markdown =
            "LEFT ALPHA\n\n"
            + SceneProseMarkers.FormatOpen(scene.Id) + "\n"
            + "right alpha\n"
            + SceneProseMarkers.CloseMarker + "\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, markdown);

        // Split at boundary (disposable copy).
        var splitAt = markdown.IndexOf(SceneProseMarkers.FormatOpen(scene.Id), StringComparison.Ordinal);
        var split = await _structure.SplitChapterAtAsync(project.Id, chapter.Id, splitAt, "Tail chapter");
        Assert.Contains("LEFT ALPHA", await _chapters.LoadContentAsync(project.Id, split.LeftChapter.Id), StringComparison.Ordinal);

        // Merge adjacent (disposable copy).
        var merged = await _structure.MergeAdjacentChaptersAsync(
            project.Id,
            split.LeftChapter.Id,
            split.RightChapter.Id);
        var mergedText = await _chapters.LoadContentAsync(project.Id, merged.SurvivingChapter.Id);
        Assert.Contains("LEFT ALPHA", mergedText, StringComparison.Ordinal);
        Assert.Contains(SceneProseMarkers.FormatOpen(scene.Id), mergedText, StringComparison.Ordinal);

        // Version + mutate + restore (disposable copy).
        var version = await _versions.CreateAsync(project.Id, "Before replace", "manual disposable");
        var options = new ManuscriptSearchOptions { FindText = "ALPHA", ReplacementText = "BETA" };
        var preview = await _search.PreviewAsync(project.Id, options);
        Assert.NotEmpty(preview.Hits);
        var replace = await _search.ApplyAsync(project.Id, options, preview.Hits, confirmed: true);
        Assert.True(replace.Succeeded);
        Assert.Contains("BETA", await _chapters.LoadContentAsync(project.Id, merged.SurvivingChapter.Id), StringComparison.Ordinal);

        await _versions.RestoreAsync(project.Id, version.Id, confirmed: true);
        Assert.Contains("ALPHA", await _chapters.LoadContentAsync(project.Id, merged.SurvivingChapter.Id), StringComparison.Ordinal);
        Assert.DoesNotContain("BETA", await _chapters.LoadContentAsync(project.Id, merged.SurvivingChapter.Id), StringComparison.Ordinal);
    }

    private async Task<Project> CreateDisposableAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        return await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title,
            Author = "Tester",
            Genre = "Fantasy",
            NorthStar = "Disposable manual flow",
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
