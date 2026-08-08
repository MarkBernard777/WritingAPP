using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Domain.Manuscript;

namespace MasterBookWritingSystem.Infrastructure.Backup;

public sealed class ExportService : IExportService
{
    private readonly IProjectService _projects;
    private readonly IChapterService _chapters;
    private readonly IStoryDataService _story;
    private readonly IIdeaService _ideas;
    private readonly ISnapshotService _snapshots;

    public ExportService(
        IProjectService projects,
        IChapterService chapters,
        IStoryDataService story,
        IIdeaService ideas,
        ISnapshotService snapshots)
    {
        _projects = projects;
        _chapters = chapters;
        _story = story;
        _ideas = ideas;
        _snapshots = snapshots;
    }

    public async Task<ExportResult> ExportManuscriptDocxAsync(
        Guid projectId,
        IReadOnlyList<Guid>? chapterIdsInOrder,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        var root = ProjectRootGuard.RequireActiveRoot(_projects, projectId);
        await _snapshots
            .CreateSafetySnapshotAsync(
                projectId,
                SafetySnapshotReason.CompilationOrExport,
                cancellationToken,
                progress)
            .ConfigureAwait(false);

        var all = await _chapters.GetAllAsync(projectId, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Chapter> ordered;
        if (chapterIdsInOrder is null || chapterIdsInOrder.Count == 0)
        {
            ordered = all;
        }
        else
        {
            var map = all.ToDictionary(item => item.Id);
            ordered = chapterIdsInOrder.Select(id =>
                map.TryGetValue(id, out var chapter)
                    ? chapter
                    : throw new InvalidOperationException($"Chapter '{id}' was not found.")).ToList();
        }

        if (ordered.Count == 0)
        {
            throw new InvalidOperationException("No chapters available to export.");
        }

        var fileName = ExportFileNames.Timestamped("manuscript", ".docx");
        var absolute = ProjectRootGuard.EnsureUniqueExportPath(root, fileName);
        progress?.Report(new OperationProgress { Message = "Building DOCX…", PercentComplete = 10 });

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = WordprocessingDocument.Create(absolute, WordprocessingDocumentType.Document);
            var main = document.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());

            for (var index = 0; index < ordered.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chapter = ordered[index];
                var markdown = _chapters.LoadContentAsync(projectId, chapter.Id, cancellationToken)
                    .GetAwaiter().GetResult();

                body.AppendChild(CreateHeading(chapter.Title));
                foreach (var paragraph in MarkdownToParagraphs(markdown))
                {
                    body.AppendChild(paragraph);
                }

                if (index < ordered.Count - 1)
                {
                    body.AppendChild(new Paragraph(
                        new Run(new Break { Type = BreakValues.Page })));
                }

                progress?.Report(new OperationProgress
                {
                    Message = $"Exported chapter {index + 1}/{ordered.Count}",
                    PercentComplete = 10 + (80.0 * (index + 1) / ordered.Count),
                });
            }

            main.Document.Save();
        }, cancellationToken).ConfigureAwait(false);

        progress?.Report(new OperationProgress { Message = "DOCX export complete", PercentComplete = 100 });
        return new ExportResult
        {
            AbsolutePath = absolute,
            RelativePath = ProjectRootGuard.ToRelativeExport(root, absolute),
            Format = "DOCX",
        };
    }

    public async Task<ExportResult> ExportProjectJsonAsync(
        Guid projectId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        var root = ProjectRootGuard.RequireActiveRoot(_projects, projectId);
        var project = _projects.ActiveProject!;
        progress?.Report(new OperationProgress { Message = "Collecting project data…", PercentComplete = 20 });

        var payload = new
        {
            exportedUtc = DateTimeOffset.UtcNow,
            schemaVersion = ProjectSchema.CurrentVersion,
            project = new
            {
                project.Id,
                project.Title,
                project.Author,
                project.Genre,
                project.NorthStar,
                PublishingRoute = project.PublishingRoute.ToString(),
                project.CreatedUtc,
                project.LastEditedUtc,
            },
            chapters = await _chapters.GetAllAsync(projectId, cancellationToken).ConfigureAwait(false),
            characters = await _story.GetCharactersAsync(projectId, cancellationToken).ConfigureAwait(false),
            worldEntries = await _story.GetWorldEntriesAsync(projectId, cancellationToken).ConfigureAwait(false),
            beats = await _story.GetBeatsAsync(projectId, cancellationToken).ConfigureAwait(false),
            scenes = await _story.GetScenesAsync(projectId, cancellationToken).ConfigureAwait(false),
            ideas = await _ideas.GetAllAsync(projectId, cancellationToken).ConfigureAwait(false),
        };

        var fileName = ExportFileNames.Timestamped("project-data", ".json");
        var absolute = ProjectRootGuard.EnsureUniqueExportPath(root, fileName);
        var json = System.Text.Json.JsonSerializer.Serialize(payload, ManifestSerializer.JsonOptions);
        await File.WriteAllTextAsync(absolute, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        progress?.Report(new OperationProgress { Message = "JSON export complete", PercentComplete = 100 });
        return new ExportResult
        {
            AbsolutePath = absolute,
            RelativePath = ProjectRootGuard.ToRelativeExport(root, absolute),
            Format = "JSON",
        };
    }

    public async Task<ExportResult> ExportStoryDataCsvAsync(
        Guid projectId,
        CancellationToken cancellationToken = default,
        IProgress<OperationProgress>? progress = null)
    {
        var root = ProjectRootGuard.RequireActiveRoot(_projects, projectId);
        var folderName = ExportFileNames.Timestamped("story-csv", "");
        folderName = folderName.TrimEnd('.');
        var absoluteDir = ProjectRootGuard.EnsureUniqueExportPath(root, folderName);
        Directory.CreateDirectory(absoluteDir);
        progress?.Report(new OperationProgress { Message = "Writing CSV files…", PercentComplete = 20 });

        var characters = await _story.GetCharactersAsync(projectId, cancellationToken).ConfigureAwait(false);
        var world = await _story.GetWorldEntriesAsync(projectId, cancellationToken).ConfigureAwait(false);
        var beats = await _story.GetBeatsAsync(projectId, cancellationToken).ConfigureAwait(false);
        var scenes = await _story.GetScenesAsync(projectId, cancellationToken).ConfigureAwait(false);
        var ideas = await _ideas.GetAllAsync(projectId, cancellationToken).ConfigureAwait(false);

        await WriteCsvAsync(
            Path.Combine(absoluteDir, "Characters.csv"),
            characters.Select(item => new
            {
                item.Id,
                item.Name,
                item.Role,
                item.Goal,
                item.Need,
                item.Fear,
                item.Wound,
                item.FalseBelief,
                item.Contradiction,
                item.Skills,
                item.Weaknesses,
                item.Resources,
                item.RelationshipsNotes,
                item.StartingState,
                item.EndingState,
                item.BookArc,
                item.SeriesArc,
                item.IsViewpoint,
                item.SceneAppearancesNotes,
            }),
            cancellationToken).ConfigureAwait(false);

        await WriteCsvAsync(
            Path.Combine(absoluteDir, "WorldEntries.csv"),
            world.Select(item => new
            {
                item.Id,
                item.Name,
                item.Category,
                Depth = item.Depth.ToString(),
                item.Notes,
                item.TravelDistance,
                item.TravelTime,
                item.CanonicalFacts,
                item.ConflictingEntries,
            }),
            cancellationToken).ConfigureAwait(false);

        await WriteCsvAsync(
            Path.Combine(absoluteDir, "Beats.csv"),
            beats.Select(item => new
            {
                item.Id,
                item.Number,
                item.Name,
                item.Summary,
                item.ChapterRef,
                item.SceneRef,
                item.ViewpointCharacterId,
                item.Event,
                item.Cause,
                item.Consequence,
                item.ArcFunction,
                item.Theme,
                item.Escalation,
                Status = item.Status.ToString(),
            }),
            cancellationToken).ConfigureAwait(false);

        await WriteCsvAsync(
            Path.Combine(absoluteDir, "Scenes.csv"),
            scenes.Select(item => new
            {
                item.Id,
                item.SequenceNumber,
                item.Title,
                item.ChapterId,
                item.ViewpointCharacterId,
                item.Location,
                item.Time,
                item.Goal,
                item.Opposition,
                item.Stakes,
                item.MainEvent,
                item.Revelation,
                item.EmotionalTurn,
                item.Choice,
                item.Outcome,
                item.Consequence,
                item.SetupObligations,
                item.PayoffObligations,
                item.NextSceneId,
                Status = item.Status.ToString(),
                item.WordCount,
            }),
            cancellationToken).ConfigureAwait(false);

        await WriteCsvAsync(
            Path.Combine(absoluteDir, "Ideas.csv"),
            ideas.Select(item => new
            {
                item.Id,
                item.Title,
                item.Notes,
                item.Decision,
            }),
            cancellationToken).ConfigureAwait(false);

        await WriteCsvAsync(
            Path.Combine(absoluteDir, "IdeaScores.csv"),
            ideas.Where(item => item.Score is not null).Select(item => new
            {
                item.Score!.Id,
                item.Score.IdeaId,
                IdeaTitle = item.Title,
                item.Score.Fascination,
                item.Score.EmotionalPower,
                item.Score.Conflict,
                item.Score.Character,
                item.Score.Visual,
                item.Score.OriginalCombination,
                item.Score.NovelLength,
                item.Score.DifficultChoices,
                item.Score.AudienceFit,
                item.Score.SeriesFit,
                item.Score.Total,
                item.Score.Decision,
            }),
            cancellationToken).ConfigureAwait(false);

        progress?.Report(new OperationProgress { Message = "CSV export complete", PercentComplete = 100 });
        return new ExportResult
        {
            AbsolutePath = absoluteDir,
            RelativePath = ProjectRootGuard.ToRelativeExport(root, absoluteDir),
            Format = "CSV",
        };
    }

    private static async Task WriteCsvAsync<T>(string path, IEnumerable<T> rows, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, Encoding.UTF8);
        await using var csv = new CsvHelper.CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture);
        await csv.WriteRecordsAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    private static Paragraph CreateHeading(string title)
    {
        return new Paragraph(
            new Run(new RunProperties(new Bold(), new FontSize { Val = "32" }), new Text(title)));
    }

    private static IEnumerable<Paragraph> MarkdownToParagraphs(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                yield return new Paragraph();
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                yield return new Paragraph(
                    new Run(new RunProperties(new Bold(), new FontSize { Val = "28" }), new Text(line[2..].Trim())));
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                yield return new Paragraph(
                    new Run(new RunProperties(new Bold(), new FontSize { Val = "26" }), new Text(line[3..].Trim())));
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                yield return new Paragraph(
                    new Run(new RunProperties(new Bold(), new FontSize { Val = "24" }), new Text(line[4..].Trim())));
                continue;
            }

            if (line == "---")
            {
                yield return new Paragraph(new Run(new Text("***")));
                continue;
            }

            yield return new Paragraph(new Run(new Text(line)));
        }
    }
}
