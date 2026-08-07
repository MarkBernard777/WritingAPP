using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Persistence;

public sealed class ProjectWorkspaceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterFileStore _chapters;
    private readonly IApplicationPaths _paths;

    public ProjectWorkspaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var services = new ServiceCollection();
        services.AddInfrastructure();
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterFileStore>();
        _paths = _provider.GetRequiredService<IApplicationPaths>();
    }

    public void Dispose()
    {
        _projects.CloseAsync().GetAwaiter().GetResult();
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; locked temp files should not fail the suite.
            }
        }
    }

    [Fact]
    public async Task Create_WritesPortableLayout_Database_AndProjectJson()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);

        var project = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Oath of Embers",
            Author = "A. Writer",
            Genre = "Epic Fantasy",
        });

        Assert.Equal("Oath of Embers", project.Title);
        Assert.True(Directory.Exists(project.RootPath));
        Assert.True(File.Exists(Path.Combine(project.RootPath, ProjectPaths.DatabaseFileName)));
        Assert.True(File.Exists(Path.Combine(project.RootPath, ProjectPaths.MetadataFileName)));
        Assert.True(Directory.Exists(Path.Combine(project.RootPath, "09 Draft", "Chapters")));
        Assert.DoesNotContain(
            Path.GetFullPath(_paths.LocalAppDataDirectory),
            Path.GetFullPath(project.RootPath),
            StringComparison.OrdinalIgnoreCase);

        var json = await File.ReadAllTextAsync(Path.Combine(project.RootPath, ProjectPaths.MetadataFileName));
        Assert.Contains(project.Id.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Oath of Embers", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_RejectsTargetInsideLocalAppData()
    {
        var forbidden = Path.Combine(_paths.LocalAppDataDirectory, "Books");
        Directory.CreateDirectory(forbidden);

        await Assert.ThrowsAsync<InvalidProjectLocationException>(() => _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = forbidden,
            Title = "Forbidden",
            Author = "A. Writer",
        }));
    }

    [Fact]
    public async Task Create_DoesNotOverwriteExistingProject()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);

        await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Existing Book",
            Author = "A. Writer",
        });
        await _projects.CloseAsync();

        await Assert.ThrowsAsync<ProjectAlreadyExistsException>(() => _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Existing Book",
            Author = "A. Writer",
        }));
    }

    [Fact]
    public async Task Open_ReopensCreatedProject()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);

        var created = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Reopen Me",
            Author = "A. Writer",
            Genre = "Mystery",
            NorthStar = "Truth over comfort",
        });
        var root = created.RootPath;
        var id = created.Id;
        await _projects.CloseAsync();
        Assert.Null(_projects.ActiveProject);

        var opened = await _projects.OpenAsync(root);

        Assert.Equal(id, opened.Id);
        Assert.Equal("Reopen Me", opened.Title);
        Assert.Equal("Mystery", opened.Genre);
        Assert.Equal("Truth over comfort", opened.NorthStar);
        Assert.Equal(root, opened.RootPath);
        Assert.NotNull(_projects.ActiveProject);
    }

    [Fact]
    public async Task Close_ClearsActiveProject()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);

        await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Closeable",
            Author = "A. Writer",
        });

        await _projects.CloseAsync();

        Assert.Null(_projects.ActiveProject);
    }

    [Fact]
    public async Task Validate_ReportsHealthyProject()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);

        var project = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Healthy",
            Author = "A. Writer",
        });

        var result = await _projects.ValidateAsync(project.RootPath);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(ProjectSchema.CurrentVersion, result.SchemaVersion);
    }

    [Fact]
    public async Task Move_ThenOpen_SucceedsWithRelativeChapterPaths()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);

        var project = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Movable",
            Author = "A. Writer",
        });

        var chapter = new Chapter
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 1,
            Title = "Chapter One",
            RelativeMarkdownPath = Path.Combine("09 Draft", "Chapters", "01-chapter-one.md"),
        };

        await _chapters.SaveAsync(project.RootPath, chapter, "# Chapter One\n\nOnce upon a time.\n");
        await _projects.CloseAsync();

        var movedRoot = Path.Combine(_tempRoot, "relocated", "Movable");
        Directory.CreateDirectory(Path.GetDirectoryName(movedRoot)!);
        Directory.Move(project.RootPath, movedRoot);

        var opened = await _projects.OpenAsync(movedRoot);
        var markdown = await _chapters.LoadAsync(movedRoot, chapter);

        Assert.Equal(project.Id, opened.Id);
        Assert.Equal(movedRoot, opened.RootPath);
        Assert.Contains("Once upon a time", markdown, StringComparison.Ordinal);
        Assert.False(Path.IsPathRooted(chapter.RelativeMarkdownPath));
    }

    [Fact]
    public async Task Recovery_MissingDatabase_IsDetected_AndRecreatedProjectOpens()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);

        var project = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Recoverable",
            Author = "A. Writer",
        });
        var root = project.RootPath;
        await _projects.CloseAsync();

        File.Delete(Path.Combine(root, ProjectPaths.DatabaseFileName));

        var validation = await _projects.ValidateAsync(root);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("project.mbws", StringComparison.OrdinalIgnoreCase));

        Directory.Delete(root, recursive: true);

        var recreated = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Recoverable",
            Author = "A. Writer",
        });

        Assert.True(File.Exists(Path.Combine(recreated.RootPath, ProjectPaths.DatabaseFileName)));
        var opened = await _projects.OpenAsync(recreated.RootPath);
        Assert.Equal("Recoverable", opened.Title);
    }

    [Fact]
    public async Task Chapter_IsStoredAsUtf8Markdown_OutsideSqlite()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);

        var project = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Manuscript",
            Author = "A. Writer",
        });

        var chapter = new Chapter
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 2,
            Title = "Chapter Two",
            RelativeMarkdownPath = Path.Combine("09 Draft", "Chapters", "02-chapter-two.md"),
        };

        const string content = "# Chapter Two\n\nUnicode — café — 字\n";
        await _chapters.SaveAsync(project.RootPath, chapter, content);

        var absolute = Path.Combine(project.RootPath, chapter.RelativeMarkdownPath);
        Assert.True(File.Exists(absolute));

        var bytes = await File.ReadAllBytesAsync(absolute);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = await File.ReadAllTextAsync(absolute);
        Assert.Equal(content, text);

        await using var db = ProjectDbContextFactory.Create(Path.Combine(project.RootPath, ProjectPaths.DatabaseFileName));
        var stored = await db.Chapters.FindAsync([chapter.Id]);
        Assert.NotNull(stored);
        Assert.Equal(chapter.RelativeMarkdownPath.Replace('\\', '/'), stored!.RelativeMarkdownPath.Replace('\\', '/'));
        Assert.DoesNotContain("café", stored.RelativeMarkdownPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SchemaVersion_IsTrackedAfterCreate()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);

        var project = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Versioned",
            Author = "A. Writer",
        });

        await using var db = ProjectDbContextFactory.Create(Path.Combine(project.RootPath, ProjectPaths.DatabaseFileName));
        var versions = db.SchemaVersions.OrderBy(v => v.Version).ToList();

        Assert.Contains(versions, v => v.Version == ProjectSchema.CurrentVersion);
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
    }
}
