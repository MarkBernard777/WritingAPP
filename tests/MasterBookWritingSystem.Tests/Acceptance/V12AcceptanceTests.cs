using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Diagnostics;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Drafting;
using MasterBookWritingSystem.Core.Manuscript;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Acceptance;

/// <summary>
/// v1.2 acceptance scenarios required for release readiness (PRODUCT_SPEC §6 + V1.2_DESIGN gates).
/// </summary>
public sealed class V12AcceptanceTests : IDisposable
{
    private const string SecretSceneDraft = "SECRET_SCENE_ASSOCIATED_DRAFT_body_never_log_this_phrase";

    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IStoryDataService _story;
    private readonly IManuscriptHierarchyService _hierarchy;
    private readonly IManuscriptSearchReplaceService _search;
    private readonly IChapterStructureService _structure;
    private readonly IDraftingTimerService _timer;
    private readonly IDraftingSessionRepository _sessions;
    private readonly IDraftingProgressQueryService _progress;
    private readonly IRecoveryJournalService _journal;
    private readonly IRecoveryCentreService _recovery;
    private readonly ISnapshotService _snapshots;
    private readonly IStoryChangeNotifier _changes;

    public V12AcceptanceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-v12-acceptance", Guid.NewGuid().ToString("N"));
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
        _search = _provider.GetRequiredService<IManuscriptSearchReplaceService>();
        _structure = _provider.GetRequiredService<IChapterStructureService>();
        _timer = _provider.GetRequiredService<IDraftingTimerService>();
        _sessions = _provider.GetRequiredService<IDraftingSessionRepository>();
        _progress = _provider.GetRequiredService<IDraftingProgressQueryService>();
        _journal = _provider.GetRequiredService<IRecoveryJournalService>();
        _recovery = _provider.GetRequiredService<IRecoveryCentreService>();
        _snapshots = _provider.GetRequiredService<ISnapshotService>();
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
    public async Task PlanAssignDraftCompile_SingleCanonicalScene_NoDuplicates()
    {
        var project = await CreateAsync("PlanDraft");
        var chapter = await _chapters.CreateAsync(project.Id, "Host");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 1,
            Title = "Planned",
            Status = SceneDraftStatus.Outlined,
        });

        await _hierarchy.AssignSceneToChapterAsync(project.Id, scene.Id, chapter.Id);
        var markdown = await _chapters.LoadContentAsync(project.Id, chapter.Id);
        var drafted = markdown.Replace(
            SceneProseMarkers.FormatOpen(scene.Id) + "\n",
            SceneProseMarkers.FormatOpen(scene.Id) + "\nDrafted prose for acceptance.\n",
            StringComparison.Ordinal);
        await _chapters.SaveContentAsync(project.Id, chapter.Id, drafted);

        var scenes = await _story.GetScenesAsync(project.Id);
        Assert.Single(scenes, item => item.Id == scene.Id);
        Assert.Equal(1, scenes.Count(item => item.Title == "Planned"));

        var tree = await _hierarchy.GetHierarchyAsync(project.Id);
        var treeScenes = tree.Books.SelectMany(b => b.Parts)
            .SelectMany(p => p.Chapters)
            .SelectMany(c => c.Scenes)
            .Where(s => s.Id == scene.Id)
            .ToList();
        Assert.Single(treeScenes);

        var compile = await _chapters.CompileSelectedAsync(project.Id, [chapter.Id], "planned.md");
        var exported = await File.ReadAllTextAsync(compile.AbsolutePath);
        Assert.Contains("Drafted prose for acceptance", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("mbws:scene", exported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SceneUpdate_IsVisibleAcrossStoryDataHierarchyAndNotifierSurfaces()
    {
        var project = await CreateAsync("Sync");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "Original",
        });

        StoryChangeEventArgs? last = null;
        void Handler(object? _, StoryChangeEventArgs args) => last = args;
        _changes.Changed += Handler;
        try
        {
            scene.Title = "Renamed everywhere";
            scene.Status = SceneDraftStatus.Drafted;
            await _story.UpdateSceneAsync(project.Id, scene);

            Assert.NotNull(last);
            Assert.Equal(StoryChangeKind.SceneUpserted, last!.Kind);

            var fromStory = (await _story.GetScenesAsync(project.Id)).Single(item => item.Id == scene.Id);
            Assert.Equal("Renamed everywhere", fromStory.Title);
            Assert.Equal(SceneDraftStatus.Drafted, fromStory.Status);

            var tree = await _hierarchy.GetHierarchyAsync(project.Id);
            var fromTree = tree.Books.SelectMany(b => b.Parts)
                .SelectMany(p => p.Chapters)
                .SelectMany(c => c.Scenes)
                .Single(s => s.Id == scene.Id);
            Assert.Equal("Renamed everywhere", fromTree.Title);
        }
        finally
        {
            _changes.Changed -= Handler;
        }
    }

    [Fact]
    public async Task HierarchyOrdering_PersistsBookPartChapterScene_AcrossCloseReopen()
    {
        var project = await CreateAsync("Order");
        var bookB = await _hierarchy.CreateBookAsync(project.Id, "Book B");
        await _hierarchy.MoveBookAsync(project.Id, bookB.Id, direction: -1);
        var part2 = await _hierarchy.CreatePartAsync(project.Id, bookB.Id, "Part 2");
        await _hierarchy.MovePartAsync(project.Id, part2.Id, direction: -1);

        var c1 = await _chapters.CreateAsync(project.Id, "One");
        var c2 = await _chapters.CreateAsync(project.Id, "Two");
        await _hierarchy.MoveChapterToPartAsync(project.Id, c1.Id, part2.Id);
        await _hierarchy.MoveChapterToPartAsync(project.Id, c2.Id, part2.Id);
        await _chapters.MoveUpAsync(project.Id, c2.Id);

        var s1 = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = c2.Id,
            SequenceNumber = 1,
            Title = "S1",
        });
        var s2 = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = c2.Id,
            SequenceNumber = 2,
            Title = "S2",
        });
        await _hierarchy.MoveSceneAsync(project.Id, s2.Id, direction: -1);

        var root = project.RootPath;
        await _projects.CloseAsync();
        var reopened = await _projects.OpenAsync(root);
        var tree = await _hierarchy.GetHierarchyAsync(reopened.Id);

        Assert.Equal("Book B", tree.Books[0].Title);
        Assert.Equal("Part 2", tree.Books[0].Parts[0].Title);
        var chapters = tree.Books[0].Parts[0].Chapters;
        Assert.Equal(["Two", "One"], chapters.Select(c => c.Title));
        Assert.Equal(["S2", "S1"], chapters[0].Scenes.Select(s => s.Title));
        Assert.Equal(s2.Id, chapters[0].Scenes[0].Id);
        Assert.Equal(s1.Id, chapters[0].Scenes[1].Id);
    }

    [Fact]
    public async Task UpgradeFromSchemaV7_PreservesProseBytes()
    {
        var root = Path.Combine(_tempRoot, "v7-upgrade");
        Directory.CreateDirectory(root);
        foreach (var relative in ProjectPaths.StandardDirectories)
        {
            Directory.CreateDirectory(Path.Combine(root, relative));
        }

        var projectId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var relativeMd = "09 Draft/Chapters/01-legacy.md";
        var absoluteMd = Path.Combine(root, relativeMd.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteMd)!);
        var payload = "# Legacy\n\nUnique acceptance prose 9c2e1b.\n"u8.ToArray();
        await File.WriteAllBytesAsync(absoluteMd, payload);

        var dbPath = Path.Combine(root, ProjectPaths.DatabaseFileName);
        await using (var context = ProjectDbContextFactory.Create(dbPath))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(ProjectSchema.PostPublicationTrackingMigrationName);
            context.Projects.Add(new ProjectRecord
            {
                Id = projectId,
                Title = "V7 Acc",
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
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Chapters (Id, ProjectId, SequenceNumber, Title, RelativeMarkdownPath, WordCount, ContentHash)
                VALUES ({0}, {1}, 1, 'Legacy', {2}, 4, 'abc');
                """,
                chapterId,
                projectId,
                relativeMd);
        }

        SqliteConnection.ClearAllPools();
        await File.WriteAllTextAsync(
            Path.Combine(root, ProjectPaths.MetadataFileName),
            $$"""
            {
              "id": "{{projectId}}",
              "title": "V7 Acc",
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
        Assert.Equal(payload, await File.ReadAllBytesAsync(absoluteMd));
        Assert.Contains("Unique acceptance prose", await _chapters.LoadContentAsync(opened.Id, chapterId), StringComparison.Ordinal);
        var snapshots = await _snapshots.ListSnapshotsAsync(opened.Id);
        Assert.Contains(snapshots, item =>
            item.Name.StartsWith("safety-migration-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnstructuredLegacyChapter_RemainsEditableWithoutMarkers()
    {
        var project = await CreateAsync("LegacyEdit");
        var chapter = await _chapters.CreateAsync(project.Id, "Loose");
        const string body = "# Loose\n\nNo markers here.\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, body);
        await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "Outline only",
        });

        var edited = body + "\nExtra line.\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, edited);
        var loaded = await _chapters.LoadContentAsync(project.Id, chapter.Id);
        Assert.Equal(edited, loaded);
        Assert.DoesNotContain("mbws:scene", loaded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrashRecovery_ProtectsSceneAssociatedDraft()
    {
        var project = await CreateAsync("CrashScene");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "S",
        });
        var baseline =
            SceneProseMarkers.FormatOpen(scene.Id) + "\n"
            + "baseline\n"
            + SceneProseMarkers.CloseMarker + "\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, baseline);

        var draft =
            SceneProseMarkers.FormatOpen(scene.Id) + "\n"
            + SecretSceneDraft + "\n"
            + SceneProseMarkers.CloseMarker + "\n";
        var entryId = RecoveryJournalIds.Create(project.Id, RecoveryEntityType.ManuscriptChapter, chapter.Id);
        await _journal.UpsertAsync(new RecoveryJournalEntry
        {
            EntryId = entryId,
            ProjectId = project.Id,
            EntityType = RecoveryEntityType.ManuscriptChapter,
            EntityId = chapter.Id,
            DraftText = draft,
        });

        var root = project.RootPath;
        await _projects.CloseAsync();
        var reopened = await _projects.OpenAsync(root);
        Assert.True(await _journal.HasRecoverableEntriesAsync(reopened.Id));
        var recovered = await _recovery.RecoverJournalToCopyAsync(root, entryId);
        Assert.True(recovered.Succeeded, recovered.Message);
        Assert.Contains(SecretSceneDraft, await File.ReadAllTextAsync(recovered.OutputPath!), StringComparison.Ordinal);
        Assert.Contains("baseline", await _chapters.LoadContentAsync(reopened.Id, chapter.Id), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlobalReplace_PreviewExact_AndRollback()
    {
        var project = await CreateAsync("Replace");
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "alpha beta alpha\n");
        var options = new ManuscriptSearchOptions { FindText = "alpha", ReplacementText = "ALPHA" };
        var preview = await _search.PreviewAsync(project.Id, options);
        Assert.Equal(2, preview.Hits.Count);
        Assert.All(preview.Hits, hit =>
        {
            Assert.Equal("alpha", hit.MatchedText);
            Assert.Equal("ALPHA", hit.ProposedReplacement);
            Assert.Equal(chapter.Id, hit.ChapterId);
        });

        preview.Hits[1].Include = false;
        var result = await _search.ApplyAsync(project.Id, options, preview.Hits, confirmed: true);
        Assert.Equal("ALPHA beta alpha\n", await _chapters.LoadContentAsync(project.Id, chapter.Id));
        await _search.RollbackAsync(project.Id, result.RollbackToken!.Value, confirmed: true);
        Assert.Equal("alpha beta alpha\n", await _chapters.LoadContentAsync(project.Id, chapter.Id));
    }

    [Fact]
    public async Task InvalidSplit_LeavesDatabaseAndFilesUnchanged()
    {
        var project = await CreateAsync("SplitSafe");
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
            "LEFT\n"
            + SceneProseMarkers.FormatOpen(scene.Id) + "\n"
            + "inside\n"
            + SceneProseMarkers.CloseMarker + "\n";
        await _chapters.SaveContentAsync(project.Id, chapter.Id, markdown);
        var beforeChapters = (await _chapters.GetAllAsync(project.Id)).Count;
        var cut = markdown.IndexOf("inside", StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _structure.SplitChapterAtAsync(project.Id, chapter.Id, cut));

        Assert.Equal(markdown, await _chapters.LoadContentAsync(project.Id, chapter.Id));
        Assert.Equal(beforeChapters, (await _chapters.GetAllAsync(project.Id)).Count);
        Assert.Equal(chapter.Id, (await _story.GetScenesAsync(project.Id)).Single(s => s.Id == scene.Id).ChapterId);
    }

    [Fact]
    public async Task WritingSession_LogsWithoutBlockingSubsequentChapterSave()
    {
        var project = await CreateAsync("Session");
        await _timer.OnProjectOpenedAsync(project.Id);
        var chapter = await _chapters.CreateAsync(project.Id, "Ch");
        await _timer.NoteManuscriptSaveAsync(project.Id, chapter.Id, "one two");
        await _timer.NoteManuscriptSaveAsync(project.Id, chapter.Id, "one two three four");

        // Editor path remains usable immediately after auto-session logging.
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "one two three four five six\n");
        Assert.Equal(DraftingTimerState.Running, _timer.GetSnapshot().State);
        Assert.Contains(
            await _sessions.ListAsync(project.Id),
            item => item.ChapterId == chapter.Id && item.EndedUtc is null);
        Assert.Equal("one two three four five six\n", await _chapters.LoadContentAsync(project.Id, chapter.Id));
    }

    [Fact]
    public async Task ProgressTotals_ByDateChapterSceneViewpoint_AreCorrect()
    {
        var project = await CreateAsync("Progress");
        var chapter = await _chapters.CreateAsync(project.Id, "Host");
        var character = await _story.CreateCharacterAsync(project.Id, new Character
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Ava",
            IsViewpoint = true,
        });
        var scene = await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SequenceNumber = 1,
            Title = "Beat",
            ViewpointCharacterId = character.Id,
        });

        var started = DateTimeOffset.UtcNow;
        await _sessions.UpsertAsync(new DraftingSession
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            ChapterId = chapter.Id,
            SceneId = scene.Id,
            ViewpointCharacterId = character.Id,
            ViewpointLabel = "Ava",
            StartedUtc = started,
            EndedUtc = started.AddMinutes(12),
            LastActivityUtc = started.AddMinutes(12),
            ActiveDuration = TimeSpan.FromMinutes(10),
            StartingWordCount = 10,
            EndingWordCount = 35,
            NetWordChange = 25,
            CompletionReason = DraftingSessionCompletionReason.ManualStop,
        });

        var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(started, TimeZoneInfo.Local).DateTime);
        var report = await _progress.GetProgressAsync(project.Id, local, local);
        Assert.Equal(25, report.NetWords);
        Assert.Single(report.ByDate);
        Assert.Equal(25, Assert.Single(report.ByChapter).NetWords);
        Assert.Equal(25, Assert.Single(report.ByScene).NetWords);
        Assert.Equal("Ava", Assert.Single(report.ByViewpoint).Label);
        Assert.Equal(25, report.ByViewpoint[0].NetWords);
    }

    [Fact]
    public async Task Compile_UsesHierarchyChapterOrder_AndStripsMarkers()
    {
        var project = await CreateAsync("CompileOrder");
        var book = (await _hierarchy.GetHierarchyAsync(project.Id)).Books[0];
        var part = await _hierarchy.CreatePartAsync(project.Id, book.Id, "Later Part");
        var early = await _chapters.CreateAsync(project.Id, "Early");
        var late = await _chapters.CreateAsync(project.Id, "Late");
        await _hierarchy.MoveChapterToPartAsync(project.Id, late.Id, part.Id);

        var sceneId = Guid.NewGuid();
        await _chapters.SaveContentAsync(
            project.Id,
            early.Id,
            "AAA\n" + SceneProseMarkers.FormatOpen(sceneId) + "\nmarker body\n" + SceneProseMarkers.CloseMarker + "\n");
        await _chapters.SaveContentAsync(project.Id, late.Id, "BBB\n");

        var tree = await _hierarchy.GetHierarchyAsync(project.Id);
        var orderedIds = tree.Books
            .OrderBy(b => b.SequenceNumber)
            .SelectMany(b => b.Parts.OrderBy(p => p.SequenceNumber))
            .SelectMany(p => p.Chapters.OrderBy(c => c.SequenceNumber))
            .Select(c => c.Id)
            .Where(id => id == early.Id || id == late.Id)
            .ToList();

        // Early stays on default part (sequence before Later Part when default part is first).
        var result = await _chapters.CompileSelectedAsync(project.Id, orderedIds, "order.md");
        var text = await File.ReadAllTextAsync(result.AbsolutePath);
        Assert.True(
            text.IndexOf("AAA", StringComparison.Ordinal) < text.IndexOf("BBB", StringComparison.Ordinal));
        Assert.DoesNotContain("mbws:scene", text, StringComparison.Ordinal);
        Assert.Contains("marker body", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticLogs_NeverIncludeManuscriptProseOrSceneMarkers()
    {
        var sanitized = DiagnosticTextSanitizer.Sanitize(
            "Error near " + SecretSceneDraft + " " + SceneProseMarkers.CloseMarker);
        Assert.DoesNotContain(SecretSceneDraft, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("mbws:scene", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redacted", sanitized, StringComparison.OrdinalIgnoreCase);

        var markerOnly = DiagnosticTextSanitizer.Sanitize(
            "Parse failed around " + SceneProseMarkers.FormatOpen(Guid.NewGuid()));
        Assert.DoesNotContain("mbws:scene", markerOnly, StringComparison.OrdinalIgnoreCase);
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
            NorthStar = "v1.2 acceptance",
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
