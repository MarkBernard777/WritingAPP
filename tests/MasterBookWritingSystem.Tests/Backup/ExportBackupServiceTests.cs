using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Domain.Tools;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Backup;

public sealed class ExportBackupServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IStoryDataService _story;
    private readonly IIdeaService _ideas;
    private readonly IExportService _exports;
    private readonly ISnapshotService _snapshots;
    private readonly IPortablePackageService _packages;

    public ExportBackupServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _chapters = _provider.GetRequiredService<IChapterService>();
        _story = _provider.GetRequiredService<IStoryDataService>();
        _ideas = _provider.GetRequiredService<IIdeaService>();
        _exports = _provider.GetRequiredService<IExportService>();
        _snapshots = _provider.GetRequiredService<ISnapshotService>();
        _packages = _provider.GetRequiredService<IPortablePackageService>();
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
    public async Task DocxExport_PreservesChapterOrder_AndPageBreaks()
    {
        var project = await CreateAsync("Docx");
        var first = await _chapters.CreateAsync(project.Id, "Alpha");
        var second = await _chapters.CreateAsync(project.Id, "Beta");
        await _chapters.SaveContentAsync(project.Id, first.Id, "# Alpha\n\nFirst body.\n");
        await _chapters.SaveContentAsync(project.Id, second.Id, "# Beta\n\nSecond body.\n");

        var result = await _exports.ExportManuscriptDocxAsync(project.Id, [second.Id, first.Id]);
        Assert.Equal("DOCX", result.Format);
        Assert.True(File.Exists(result.AbsolutePath));

        using var document = WordprocessingDocument.Open(result.AbsolutePath, false);
        var mainPart = document.MainDocumentPart;
        Assert.NotNull(mainPart);
        Assert.NotNull(mainPart.Document);
        var text = mainPart.Document.InnerText;
        Assert.True(text.IndexOf("Beta", StringComparison.Ordinal) < text.IndexOf("Alpha", StringComparison.Ordinal));
        Assert.Contains("Second body", text, StringComparison.Ordinal);
        Assert.Contains("First body", text, StringComparison.Ordinal);
        Assert.Contains("w:br", mainPart.Document.OuterXml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CsvAndJson_Export_FieldCompleteness_AndNoOverwrite()
    {
        var project = await CreateAsync("CsvJson");
        await _story.CreateCharacterAsync(project.Id, new Character
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Kael, \"Captain\"",
            Goal = "Restore peace",
        });
        await _story.CreateWorldEntryAsync(project.Id, new WorldEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Ash Harbor",
            Category = "Location",
        });
        await _story.CreateBeatAsync(project.Id, new Beat
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Number = 1,
            Name = "Opening",
        });
        await _story.CreateSceneAsync(project.Id, new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SequenceNumber = 1,
            Title = "Arrival",
        });
        var idea = await _ideas.CreateAsync(project.Id, "Seed");
        await _ideas.SaveScoreAsync(project.Id, idea.Id, new IdeaScore
        {
            Id = Guid.NewGuid(),
            IdeaId = idea.Id,
            ProjectId = project.Id,
            Fascination = 8,
            EmotionalPower = 7,
            Conflict = 8,
            Character = 7,
            Visual = 6,
            OriginalCombination = 7,
            NovelLength = 8,
            DifficultChoices = 7,
            AudienceFit = 8,
            SeriesFit = 7,
        });

        var csv = await _exports.ExportStoryDataCsvAsync(project.Id);
        Assert.True(Directory.Exists(csv.AbsolutePath));
        var charactersCsv = await File.ReadAllTextAsync(Path.Combine(csv.AbsolutePath, "Characters.csv"));
        Assert.Contains("Kael", charactersCsv, StringComparison.Ordinal);
        Assert.Contains("\"\"", charactersCsv, StringComparison.Ordinal); // escaped quotes
        Assert.True(File.Exists(Path.Combine(csv.AbsolutePath, "IdeaScores.csv")));

        var json = await _exports.ExportProjectJsonAsync(project.Id);
        var jsonText = await File.ReadAllTextAsync(json.AbsolutePath);
        Assert.Contains("Kael", jsonText, StringComparison.Ordinal);
        Assert.Contains("Ash Harbor", jsonText, StringComparison.Ordinal);

        var exporter = _provider.GetRequiredService<IToolsReportExporter>();
        await exporter.ExportMarkdownAsync(project.Id, "fixed-report.md", "# once\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.ExportMarkdownAsync(project.Id, "fixed-report.md", "# twice\n"));
    }

    [Fact]
    public async Task Snapshot_CreateValidate_AndRejectCorruption()
    {
        var project = await CreateAsync("Snap");
        await _chapters.CreateAsync(project.Id, "One");
        var created = await _snapshots.CreateSnapshotAsync(project.Id);
        Assert.True(created.IsValid);
        Assert.True(created.Manifest.Files.Count > 0);
        Assert.Contains(created.Manifest.Files, item => item.RelativePath.Equals("project.mbws", StringComparison.OrdinalIgnoreCase));

        var listed = await _snapshots.ListSnapshotsAsync(project.Id);
        Assert.Contains(listed, item => item.Name == created.Name && item.IsValid);

        var corruptFile = Path.Combine(created.DirectoryPath, created.Manifest.Files[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
        await File.AppendAllTextAsync(corruptFile, "corrupt");
        var preview = await _snapshots.PreviewRestoreAsync(project.Id, created.DirectoryPath);
        Assert.False(preview.CanRestore);
        Assert.Contains(preview.ValidationErrors, error => error.Contains("Checksum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RestoreFailure_DoesNotDamageCurrentProject()
    {
        var project = await CreateAsync("SafeRestore");
        var chapter = await _chapters.CreateAsync(project.Id, "KeepMe");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "original content");
        var snapshot = await _snapshots.CreateSnapshotAsync(project.Id);

        // Corrupt snapshot after creation.
        var target = Path.Combine(snapshot.DirectoryPath, "project.mbws");
        await File.WriteAllBytesAsync(target, Encoding.UTF8.GetBytes("not-a-database"));

        var result = await _snapshots.RestoreSnapshotAsync(project.Id, snapshot.DirectoryPath, confirmed: true);
        Assert.False(result.Succeeded);

        var content = await _chapters.LoadContentAsync(project.Id, chapter.Id);
        Assert.Contains("original content", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restore_RequiresConfirmation()
    {
        var project = await CreateAsync("Confirm");
        var snapshot = await _snapshots.CreateSnapshotAsync(project.Id);
        var result = await _snapshots.RestoreSnapshotAsync(project.Id, snapshot.DirectoryPath, confirmed: false);
        Assert.False(result.Succeeded);
        Assert.Contains("confirmation", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Zip_RejectsTraversal_AndSupportsRoundTrip()
    {
        var project = await CreateAsync("Zip");
        await _chapters.CreateAsync(project.Id, "Traveler");
        var exported = await _packages.ExportZipAsync(project.Id);
        Assert.True(File.Exists(exported.AbsolutePath));

        var evilZip = Path.Combine(_tempRoot, "evil.zip");
        using (var archive = ZipFile.Open(evilZip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("nope");
        }

        var evilDest = Path.Combine(_tempRoot, "evil-dest");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _packages.ImportZipAsync(evilZip, evilDest, overwriteExisting: false));

        var importDest = Path.Combine(_tempRoot, "imported-project");
        await _packages.ImportZipAsync(exported.AbsolutePath, importDest, overwriteExisting: false);
        Assert.True(File.Exists(Path.Combine(importDest, "project.mbws")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _packages.ImportZipAsync(exported.AbsolutePath, importDest, overwriteExisting: false));
    }

    [Fact]
    public async Task SuccessfulRestore_RoundTripsChapterContent()
    {
        var project = await CreateAsync("RoundTrip");
        var chapter = await _chapters.CreateAsync(project.Id, "Chapter");
        await _chapters.SaveContentAsync(project.Id, chapter.Id, "version-one");
        var snapshot = await _snapshots.CreateSnapshotAsync(project.Id);

        await _chapters.SaveContentAsync(project.Id, chapter.Id, "version-two");
        var result = await _snapshots.RestoreSnapshotAsync(project.Id, snapshot.DirectoryPath, confirmed: true);
        Assert.True(result.Succeeded, result.Message);

        var restoredChapter = (await _chapters.GetAllAsync(project.Id)).Single();
        var text = await _chapters.LoadContentAsync(project.Id, restoredChapter.Id);
        Assert.Contains("version-one", text, StringComparison.Ordinal);
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
