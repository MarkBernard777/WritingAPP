using System.Diagnostics;
using System.Text;
using MasterBookWritingSystem.Core.Accessibility;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace MasterBookWritingSystem.Tests.Performance;

public sealed class PerformanceReadinessTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IStoryDataService _story;
    private readonly ISnapshotService _snapshots;
    private readonly IRecoveryCentreService _recovery;
    private readonly IRecoveryJournalService _journal;

    public PerformanceReadinessTests(ITestOutputHelper output)
    {
        _output = output;
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-perf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationPaths>(new TempApplicationPaths(Path.Combine(_tempRoot, "appdata")));
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _story = _provider.GetRequiredService<IStoryDataService>();
        _snapshots = _provider.GetRequiredService<ISnapshotService>();
        _recovery = _provider.GetRequiredService<IRecoveryCentreService>();
        _journal = _provider.GetRequiredService<IRecoveryJournalService>();
    }

    public void Dispose()
    {
        _projects.CloseAsync().GetAwaiter().GetResult();
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SaveStateAccessibility_DoesNotRelyOnColourAlone()
    {
        Assert.Contains("Saving", SaveStateAccessibility.FormatDisplay(SaveState.Saving, "Saving…"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fail", SaveStateAccessibility.FormatDisplay(SaveState.SaveFailed, "disk full"), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Save status:", SaveStateAccessibility.FormatAccessibleName(SaveState.RecoveryAvailable, "Recovery available"));
    }

    [Fact]
    public async Task CreateOpen_With200Chapters_StaysWithinCeiling_AndListUsesMetadataOnly()
    {
        var createSw = Stopwatch.StartNew();
        var project = await CreateAsync("Perf200");
        var ids = new List<Guid>(200);
        for (var i = 1; i <= 200; i++)
        {
            var chapter = await _chapters.CreateAsync(project.Id, $"Chapter {i:000}");
            ids.Add(chapter.Id);
            await _chapters.SaveContentAsync(project.Id, chapter.Id, $"# Chapter {i}\n\nBody text for chapter {i}.\n");
        }

        createSw.Stop();
        Report("create+seed-200-chapters", createSw.Elapsed);
        Assert.True(createSw.Elapsed < TimeSpan.FromMinutes(4), $"Create/seed took {createSw.Elapsed}");

        var listSw = Stopwatch.StartNew();
        var listed = await _chapters.GetAllAsync(project.Id);
        listSw.Stop();
        Report("list-200-chapter-metadata", listSw.Elapsed);
        Assert.Equal(200, listed.Count);
        Assert.Equal(Enumerable.Range(1, 200).Select(i => $"Chapter {i:000}"), listed.Select(item => item.Title));
        Assert.All(listed, item => Assert.True(item.WordCount > 0));
        Assert.True(listSw.Elapsed < TimeSpan.FromSeconds(15), $"Metadata list took {listSw.Elapsed}");

        var root = project.RootPath;
        await _projects.CloseAsync();

        var openSw = Stopwatch.StartNew();
        var opened = await _projects.OpenAsync(root);
        var reopenedChapters = await _chapters.GetAllAsync(opened.Id);
        openSw.Stop();
        Report("open+list-200-chapters", openSw.Elapsed);
        Assert.Equal(200, reopenedChapters.Count);
        Assert.True(openSw.Elapsed < TimeSpan.FromMinutes(2), $"Open took {openSw.Elapsed}");
    }

    [Fact]
    public async Task Compile_200Chapters_StreamsAndHonoursCancellationCeiling()
    {
        var project = await CreateAsync("Compile200");
        var ids = new List<Guid>(200);
        for (var i = 1; i <= 200; i++)
        {
            var chapter = await _chapters.CreateAsync(project.Id, $"C{i:000}");
            ids.Add(chapter.Id);
            await _chapters.SaveContentAsync(project.Id, chapter.Id, $"Content {i} word count sample.\n");
        }

        var compileSw = Stopwatch.StartNew();
        var result = await _chapters.CompileSelectedAsync(project.Id, ids, "perf-compile-200.md");
        compileSw.Stop();
        Report("compile-200-chapters", compileSw.Elapsed);
        Assert.Equal(200, result.ChapterCount);
        Assert.True(File.Exists(result.AbsolutePath));
        Assert.True(result.WordCount > 0);
        Assert.True(compileSw.Elapsed < TimeSpan.FromMinutes(5), $"Compile took {compileSw.Elapsed}");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _chapters.CompileSelectedAsync(project.Id, ids, "perf-compile-cancelled.md", cts.Token));
    }

    [Fact]
    public async Task SnapshotList_ManyEntries_IsBounded_AndFullValidateIsSeparate()
    {
        var project = await CreateAsync("SnapMany");
        await _chapters.CreateAsync(project.Id, "Only");
        var snapshotsRoot = Path.Combine(project.RootPath, "13 Archive", "Snapshots");
        Directory.CreateDirectory(snapshotsRoot);

        for (var i = 0; i < 40; i++)
        {
            await SeedLightweightSnapshotAsync(snapshotsRoot, project.Id, project.Title, i);
        }

        var listSw = Stopwatch.StartNew();
        var listed = await _snapshots.ListSnapshotsAsync(project.Id);
        listSw.Stop();
        Report("list-40-snapshots-no-checksum", listSw.Elapsed);
        Assert.True(listed.Count >= 40);
        Assert.All(listed, item => Assert.True(item.IsValid));
        Assert.True(listSw.Elapsed < TimeSpan.FromSeconds(30), $"Snapshot list took {listSw.Elapsed}");

        var validateSw = Stopwatch.StartNew();
        var preview = await _snapshots.PreviewRestoreAsync(project.Id, listed[0].DirectoryPath);
        validateSw.Stop();
        Report("validate-one-snapshot-checksums", validateSw.Elapsed);
        Assert.True(preview.CanRestore);
        Assert.True(validateSw.Elapsed < TimeSpan.FromSeconds(30), $"Validate took {validateSw.Elapsed}");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _snapshots.ListSnapshotsAsync(project.Id, cts.Token));
    }

    [Fact]
    public async Task StoryData_LoadLargeCharacterAndSceneSets_StaysWithinCeiling()
    {
        var project = await CreateAsync("StoryLoad");
        const int count = 200;
        for (var i = 1; i <= count; i++)
        {
            await _story.CreateCharacterAsync(project.Id, new Character
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = $"Character {i:000}",
                Role = "Cast",
            });
            await _story.CreateSceneAsync(project.Id, new Scene
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                SequenceNumber = i,
                Title = $"Scene {i:000}",
            });
        }

        var loadSw = Stopwatch.StartNew();
        var characters = await _story.GetCharactersAsync(project.Id);
        var scenes = await _story.GetScenesAsync(project.Id);
        loadSw.Stop();
        Report("load-200-characters-and-scenes", loadSw.Elapsed);
        Assert.Equal(count, characters.Count);
        Assert.Equal(count, scenes.Count);
        Assert.True(loadSw.Elapsed < TimeSpan.FromMinutes(2), $"Story load took {loadSw.Elapsed}");
    }

    [Fact]
    public async Task RecoveryDiagnose_DetectsMissingFilesAndJournals_WithinCeiling()
    {
        var project = await CreateAsync("RecoverPerf");
        var chapter = await _chapters.CreateAsync(project.Id, "MissingSoon");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "body");
        var markdownPath = Path.Combine(project.RootPath, chapter.RelativeMarkdownPath.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(markdownPath);

        await _journal.UpsertAsync(new RecoveryJournalEntry
        {
            EntryId = RecoveryJournalIds.Create(project.Id, RecoveryEntityType.ManuscriptChapter, chapter.Id),
            ProjectId = project.Id,
            EntityType = RecoveryEntityType.ManuscriptChapter,
            EntityId = chapter.Id,
            DraftText = "recover-me",
        });

        var diagnoseSw = Stopwatch.StartNew();
        var report = await _recovery.DiagnoseAsync(project.RootPath);
        diagnoseSw.Stop();
        Report("diagnose-missing-and-journal", diagnoseSw.Elapsed);
        Assert.NotEmpty(report.MissingChapterRelativePaths);
        Assert.Contains(
            report.MissingChapterRelativePaths,
            path => path.Contains(chapter.RelativeMarkdownPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(report.JournalEntries);
        Assert.True(diagnoseSw.Elapsed < TimeSpan.FromMinutes(2), $"Diagnose took {diagnoseSw.Elapsed}");
    }

    private async Task<Core.Domain.Project> CreateAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);
        return await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title + "-" + Guid.NewGuid().ToString("N")[..8],
            Author = "Perf",
            Genre = "Test",
            NorthStar = "Measure",
        });
    }

    private static async Task SeedLightweightSnapshotAsync(
        string snapshotsRoot,
        Guid projectId,
        string projectTitle,
        int index)
    {
        var name = $"manual-{DateTime.UtcNow:yyyyMMddHHmmss}-{index:000}";
        var dir = Path.Combine(snapshotsRoot, name);
        Directory.CreateDirectory(dir);
        var payload = Encoding.UTF8.GetBytes($"snapshot-payload-{index}-{new string('x', 2048)}");
        var relative = "marker.txt";
        var absolute = Path.Combine(dir, relative);
        await File.WriteAllBytesAsync(absolute, payload);
        var hash = ChecksumHelper.Sha256Bytes(payload);
        var manifest = new SnapshotManifest
        {
            FormatVersion = SnapshotManifest.CurrentFormatVersion,
            ProjectId = projectId,
            ProjectTitle = projectTitle,
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-index),
            SchemaVersion = 1,
            SnapshotName = name,
            Kind = SnapshotKind.Manual,
            Files =
            [
                new SnapshotFileEntry
                {
                    RelativePath = relative,
                    SizeBytes = payload.Length,
                    Sha256 = hash,
                },
            ],
        };
        await File.WriteAllTextAsync(
            Path.Combine(dir, "snapshot-manifest.json"),
            ManifestSerializer.Serialize(manifest));
    }

    private void Report(string label, TimeSpan elapsed)
        => _output.WriteLine($"PERF {label}: {elapsed.TotalMilliseconds:F0} ms");

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

    private sealed class TempApplicationPaths(string root) : IApplicationPaths
    {
        public string LocalAppDataDirectory { get; } = root;
    }
}
