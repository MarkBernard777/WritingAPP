using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Hierarchy;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Hierarchy;

public sealed class ManuscriptHierarchyServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IStoryDataService _story;
    private readonly IManuscriptHierarchyService _hierarchy;
    private readonly ISnapshotService _snapshots;

    public ManuscriptHierarchyServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-hierarchy-tests", Guid.NewGuid().ToString("N"));
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
        _snapshots = _provider.GetRequiredService<ISnapshotService>();
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
    public async Task NewProject_ReceivesDefaultBookAndPart_WithoutDuplicates()
    {
        var project = await CreateAsync("Fresh Hierarchy");
        var tree = await _hierarchy.GetHierarchyAsync(project.Id);

        Assert.Equal(8, ProjectSchema.CurrentVersion);
        Assert.Single(tree.Books);
        Assert.Equal(project.Title, tree.Books[0].Title);
        Assert.Equal(1, tree.Books[0].SequenceNumber);
        Assert.Single(tree.Books[0].Parts);
        Assert.Equal("Part 1", tree.Books[0].Parts[0].Title);

        await _projects.CloseAsync();
        await _projects.OpenAsync(project.RootPath);
        var again = await _hierarchy.GetHierarchyAsync(project.Id);
        Assert.Single(again.Books);
        Assert.Single(again.Books[0].Parts);
    }

    [Fact]
    public async Task CreateChapter_AssignsDefaultPart_PreservesMarkdownPath()
    {
        var project = await CreateAsync("Chapter Part");
        var tree = await _hierarchy.GetHierarchyAsync(project.Id);
        var partId = tree.Books[0].Parts[0].Id;

        var chapter = await _chapters.CreateAsync(project.Id, "Alpha");
        Assert.Equal(partId, chapter.PartId);
        Assert.False(string.IsNullOrWhiteSpace(chapter.RelativeMarkdownPath));

        var absolute = Path.Combine(
            project.RootPath,
            chapter.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(absolute));
        var bytes = await File.ReadAllBytesAsync(absolute);

        var loaded = await _hierarchy.GetHierarchyAsync(project.Id);
        Assert.Contains(loaded.Books[0].Parts[0].Chapters, c => c.Id == chapter.Id);

        var after = await File.ReadAllBytesAsync(absolute);
        Assert.Equal(bytes, after);
    }

    [Fact]
    public async Task ReorderBookPartChapterScene_UsesContiguousSequences()
    {
        var project = await CreateAsync("Reorder");
        var bookA = (await _hierarchy.GetHierarchyAsync(project.Id)).Books[0];
        var bookB = await _hierarchy.CreateBookAsync(project.Id, "Book Two");
        Assert.Equal(2, bookB.SequenceNumber);

        await _hierarchy.MoveBookAsync(project.Id, bookB.Id, direction: -1);
        var books = (await _hierarchy.GetHierarchyAsync(project.Id)).Books;
        Assert.Equal(["Book Two", project.Title], books.Select(b => b.Title));
        Assert.Equal([1, 2], books.Select(b => b.SequenceNumber));

        var originalBook = books.Single(book => book.Title == project.Title);
        var part2 = await _hierarchy.CreatePartAsync(project.Id, originalBook.Id, "Part 2");
        await _hierarchy.MovePartAsync(project.Id, part2.Id, direction: -1);
        var parts = (await _hierarchy.GetHierarchyAsync(project.Id))
            .Books.Single(book => book.Id == originalBook.Id).Parts;
        Assert.Equal(["Part 2", "Part 1"], parts.Select(p => p.Title));
        Assert.Equal([1, 2], parts.Select(p => p.SequenceNumber));

        var c1 = await _chapters.CreateAsync(project.Id, "One");
        var c2 = await _chapters.CreateAsync(project.Id, "Two");
        await _hierarchy.MoveChapterToPartAsync(project.Id, c2.Id, parts[0].Id);
        await _chapters.MoveUpAsync(project.Id, c2.Id);

        var s1 = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = c1.Id,
            SequenceNumber = 1,
            Title = "S1",
        });
        var s2 = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = c1.Id,
            SequenceNumber = 2,
            Title = "S2",
        });
        // Same sequence allowed in another chapter
        await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = c2.Id,
            SequenceNumber = 1,
            Title = "Other-1",
        });

        await _hierarchy.MoveSceneAsync(project.Id, s2.Id, direction: -1);
        var scenes = await _story.GetScenesAsync(project.Id);
        var inC1 = scenes.Where(s => s.ChapterId == c1.Id).OrderBy(s => s.SequenceNumber).ToList();
        Assert.Equal([s2.Id, s1.Id], inC1.Select(s => s.Id));
        Assert.Equal([1, 2], inC1.Select(s => s.SequenceNumber));
    }

    [Fact]
    public async Task DeleteBookOrPart_RejectsUntilChildrenHandled()
    {
        var project = await CreateAsync("Delete Protect");
        var tree = await _hierarchy.GetHierarchyAsync(project.Id);
        var bookId = tree.Books[0].Id;
        var partId = tree.Books[0].Parts[0].Id;
        await _chapters.CreateAsync(project.Id, "Keep");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _hierarchy.DeletePartAsync(project.Id, partId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _hierarchy.DeleteBookAsync(project.Id, bookId));

        var otherBook = await _hierarchy.CreateBookAsync(project.Id, "Shelter");
        var shelterPart = await _hierarchy.CreatePartAsync(project.Id, otherBook.Id, "Shelter Part");

        await _hierarchy.DeletePartAsync(
            project.Id,
            partId,
            new HierarchyDeletionPolicy { MoveChaptersToPartId = shelterPart.Id });

        var afterPart = await _hierarchy.GetHierarchyAsync(project.Id);
        Assert.DoesNotContain(afterPart.Books.SelectMany(b => b.Parts), p => p.Id == partId);
        Assert.Contains(afterPart.Books.SelectMany(b => b.Parts).SelectMany(p => p.Chapters), c => c.Title == "Keep");

        var emptyBook = afterPart.Books.Single(b => b.Id == bookId);
        Assert.Empty(emptyBook.Parts);
        await _hierarchy.DeleteBookAsync(project.Id, bookId);
    }

    [Fact]
    public async Task DeletePart_WithUnassignPolicy_DoesNotDeleteChaptersOrFiles()
    {
        var project = await CreateAsync("Unassign Part");
        var tree = await _hierarchy.GetHierarchyAsync(project.Id);
        var partId = tree.Books[0].Parts[0].Id;
        var chapter = await _chapters.CreateAsync(project.Id, "Prose");
        var path = Path.Combine(
            project.RootPath,
            chapter.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar));
        var before = await File.ReadAllBytesAsync(path);

        await _hierarchy.DeletePartAsync(
            project.Id,
            partId,
            new HierarchyDeletionPolicy { UnassignChapters = true });

        var still = await _chapters.GetAsync(project.Id, chapter.Id);
        Assert.Null(still.PartId);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task AssignAndMoveSceneBetweenChapters_PreservesCanonicalScene()
    {
        var project = await CreateAsync("Scene Move");
        var a = await _chapters.CreateAsync(project.Id, "A");
        var b = await _chapters.CreateAsync(project.Id, "B");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = null,
            SequenceNumber = 1,
            Title = "Floating",
        });

        await _hierarchy.AssignSceneToChapterAsync(project.Id, scene.Id, a.Id);
        var assigned = await _story.GetSceneAsync(project.Id, scene.Id);
        Assert.Equal(a.Id, assigned.ChapterId);
        Assert.Equal(1, assigned.SequenceNumber);

        await _hierarchy.MoveSceneToChapterAsync(project.Id, scene.Id, b.Id);
        var moved = await _story.GetSceneAsync(project.Id, scene.Id);
        Assert.Equal(b.Id, moved.ChapterId);
        Assert.Equal(scene.Id, moved.Id);

        await _hierarchy.UnassignSceneAsync(project.Id, scene.Id);
        var unassigned = await _story.GetSceneAsync(project.Id, scene.Id);
        Assert.Null(unassigned.ChapterId);
    }

    [Fact]
    public async Task CrossProject_Operations_AreRejected()
    {
        var first = await CreateAsync("P1");
        var secondParent = Path.Combine(_tempRoot, "library2");
        Directory.CreateDirectory(secondParent);
        var second = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = secondParent,
            Title = "P2",
            Author = "Tester",
            Genre = "Fantasy",
            NorthStar = "Finish",
        });

        var bookId = (await _hierarchy.GetHierarchyAsync(second.Id)).Books[0].Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _hierarchy.CreatePartAsync(first.Id, bookId, "Nope"));
    }

    [Fact]
    public async Task UpgradeFromV7_BackfillsHierarchy_PreservesChapterBytesAndSceneAssignments()
    {
        var root = Path.Combine(_tempRoot, "v7-upgrade");
        Directory.CreateDirectory(root);
        foreach (var relative in ProjectPaths.StandardDirectories)
        {
            Directory.CreateDirectory(Path.Combine(root, relative));
        }

        var projectId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var relativeMd = "09 Draft/Chapters/01-legacy.md";
        var absoluteMd = Path.Combine(root, relativeMd.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteMd)!);
        var payload = "# Legacy\n\nUnique prose marker 7f3c9a.\n"u8.ToArray();
        await File.WriteAllBytesAsync(absoluteMd, payload);

        var dbPath = Path.Combine(root, ProjectPaths.DatabaseFileName);
        await using (var context = ProjectDbContextFactory.Create(dbPath))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(ProjectSchema.PostPublicationTrackingMigrationName);

            context.Projects.Add(new ProjectRecord
            {
                Id = projectId,
                Title = "V7 Book",
                Author = "Tester",
                Genre = "Fantasy",
                NorthStar = "Upgrade",
                PublishingRoute = PublishingRoute.SelfPublishing,
                CreatedUtc = DateTimeOffset.UtcNow,
                LastEditedUtc = DateTimeOffset.UtcNow,
            });
            context.SchemaVersions.Add(new SchemaVersionRecord
            {
                Version = 7,
                Name = ProjectSchema.PostPublicationTrackingMigrationName,
                AppliedUtc = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();

            // Insert via SQL so the v7 schema (no Chapters.PartId) is not mapped through the v8 model.
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Chapters (Id, ProjectId, SequenceNumber, Title, RelativeMarkdownPath, WordCount, ContentHash)
                VALUES ({0}, {1}, 1, 'Legacy', {2}, 4, 'abc');
                INSERT INTO Scenes (
                    Id, ProjectId, ChapterId, SequenceNumber, Title,
                    Location, Time, Goal, Opposition, Stakes, MainEvent, Revelation, EmotionalTurn,
                    Choice, Outcome, Consequence, SetupObligations, PayoffObligations,
                    Status, WordCount)
                VALUES (
                    {3}, {1}, {0}, 1, 'Opening',
                    '', '', '', '', '', '', '', '',
                    '', '', '', '', '',
                    0, 0);
                """,
                chapterId,
                projectId,
                relativeMd,
                sceneId);
        }

        SqliteConnection.ClearAllPools();
        await File.WriteAllTextAsync(
            Path.Combine(root, ProjectPaths.MetadataFileName),
            $$"""
            {
              "id": "{{projectId}}",
              "title": "V7 Book",
              "author": "Tester",
              "genre": "Fantasy",
              "publishingRoute": 1,
              "northStar": "Upgrade",
              "schemaVersion": 7,
              "createdUtc": "2026-01-01T00:00:00Z",
              "lastEditedUtc": "2026-01-01T00:00:00Z"
            }
            """);

        var opened = await _projects.OpenAsync(root);
        Assert.Equal(projectId, opened.Id);

        var snapshots = await _snapshots.ListSnapshotsAsync(opened.Id);
        Assert.Contains(snapshots, item =>
            item.Name.StartsWith("safety-migration-", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(payload, await File.ReadAllBytesAsync(absoluteMd));

        var tree = await _hierarchy.GetHierarchyAsync(opened.Id);
        Assert.Single(tree.Books);
        Assert.Single(tree.Books[0].Parts);
        var chapter = Assert.Single(tree.Books[0].Parts[0].Chapters);
        Assert.Equal(chapterId, chapter.Id);
        Assert.Equal(relativeMd, chapter.RelativeMarkdownPath);

        var scene = await _story.GetSceneAsync(opened.Id, sceneId);
        Assert.Equal(chapterId, scene.ChapterId);

        await using (var context = ProjectDbContextFactory.Create(dbPath))
        {
            var version = await context.SchemaVersions
                .AsNoTracking()
                .SingleAsync(item => item.Version == ProjectSchema.CurrentVersion);
            Assert.Equal(ProjectSchema.ManuscriptHierarchyMigrationName, version.Name);
        }
    }

    [Fact]
    public async Task TransactionFailure_DoesNotPartialReorderBooks()
    {
        var project = await CreateAsync("Txn");
        var bookB = await _hierarchy.CreateBookAsync(project.Id, "B");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _hierarchy.ReorderBookAsync(project.Id, bookB.Id, newSequenceNumber: 99));

        var books = (await _hierarchy.GetHierarchyAsync(project.Id)).Books;
        Assert.Equal([1, 2], books.Select(b => b.SequenceNumber));
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
