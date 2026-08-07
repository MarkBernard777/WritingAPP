using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Manuscript;

public sealed class ChapterServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IChapterFileStore _files;

    public ChapterServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-chapter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _files = _provider.GetRequiredService<IChapterFileStore>();
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
    public async Task CreateRenameDelete_AndOrdering_Work()
    {
        var project = await CreateAsync("Order");
        var first = await _chapters.CreateAsync(project.Id, "Alpha");
        var second = await _chapters.CreateAsync(project.Id, "Beta");
        var third = await _chapters.CreateAsync(project.Id, "Gamma");

        Assert.Equal([1, 2, 3], (await _chapters.GetAllAsync(project.Id)).Select(c => c.SequenceNumber));

        await _chapters.MoveUpAsync(project.Id, second.Id);
        var afterUp = await _chapters.GetAllAsync(project.Id);
        Assert.Equal(["Beta", "Alpha", "Gamma"], afterUp.Select(c => c.Title));

        await _chapters.MoveDownAsync(project.Id, first.Id);
        var afterDown = await _chapters.GetAllAsync(project.Id);
        Assert.Equal(["Beta", "Gamma", "Alpha"], afterDown.Select(c => c.Title));

        var renamed = await _chapters.RenameAsync(project.Id, third.Id, "Gamma Renamed");
        Assert.Equal("Gamma Renamed", renamed.Title);

        await _chapters.DeleteAsync(project.Id, second.Id);
        var remaining = await _chapters.GetAllAsync(project.Id);
        Assert.Equal(2, remaining.Count);
        Assert.Equal([1, 2], remaining.Select(c => c.SequenceNumber));
        Assert.DoesNotContain(remaining, c => c.Id == second.Id);
    }

    [Fact]
    public async Task SaveContent_UsesAtomicUtf8AndUpdatesWordCount()
    {
        var project = await CreateAsync("Save");
        var chapter = await _chapters.CreateAsync(project.Id, "Prologue");
        const string body = "# Prologue\n\nOnce upon a time [CHECK] dates.\n";

        var saved = await _chapters.SaveContentAsync(project.Id, chapter.Id, body);
        Assert.Equal(ManuscriptTextAnalytics.CountWords(body), saved.WordCount);

        var absolute = Path.Combine(project.RootPath, saved.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar));
        var bytes = await File.ReadAllBytesAsync(absolute);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal(body, await _chapters.LoadContentAsync(project.Id, chapter.Id));
    }

    [Fact]
    public async Task ChapterFileStore_RejectsPathTraversal()
    {
        var project = await CreateAsync("Paths");
        var chapter = await _chapters.CreateAsync(project.Id, "Safe");
        chapter.RelativeMarkdownPath = "../outside.md";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _files.SaveAsync(project.RootPath, chapter, "nope"));
    }

    [Fact]
    public async Task CompileSelected_UsesDisplayedOrder_AndProtectsOverwrite()
    {
        var project = await CreateAsync("Compile");
        var a = await _chapters.CreateAsync(project.Id, "One");
        var b = await _chapters.CreateAsync(project.Id, "Two");
        await _chapters.SaveContentAsync(project.Id, a.Id, "# One\n\nFirst body.\n");
        await _chapters.SaveContentAsync(project.Id, b.Id, "# Two\n\nSecond body.\n");

        var result = await _chapters.CompileSelectedAsync(
            project.Id,
            [b.Id, a.Id],
            "draft-compile.md");

        Assert.Equal("Exports/draft-compile.md", result.RelativePath.Replace('\\', '/'));
        Assert.True(File.Exists(result.AbsolutePath));
        var compiled = await File.ReadAllTextAsync(result.AbsolutePath);
        Assert.True(compiled.IndexOf("Second body", StringComparison.Ordinal)
            < compiled.IndexOf("First body", StringComparison.Ordinal));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _chapters.CompileSelectedAsync(project.Id, [a.Id], "draft-compile.md"));
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
