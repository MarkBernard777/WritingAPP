using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Tools;
using MasterBookWritingSystem.Core.Domain.Workflow;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Tools;

public sealed class IdeaServiceAndToolsIntegrationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IIdeaService _ideas;
    private readonly IToolsReportExporter _exporter;
    private readonly IWorkflowService _workflow;

    public IdeaServiceAndToolsIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(
            new FileWorkflowDefinitionSource(FindPath("seed", "workflow.json")));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _ideas = _provider.GetRequiredService<IIdeaService>();
        _exporter = _provider.GetRequiredService<IToolsReportExporter>();
        _workflow = _provider.GetRequiredService<IWorkflowService>();
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
    public async Task Idea_Crud_AndScorePersistence_CascadesOnDelete()
    {
        var project = await CreateAsync("Ideas");
        var created = await _ideas.CreateAsync(project.Id, "Oathbreakers", "Northern kingdoms");
        var scored = await _ideas.SaveScoreAsync(project.Id, created.Id, new IdeaScore
        {
            Id = Guid.NewGuid(),
            IdeaId = created.Id,
            ProjectId = project.Id,
            Fascination = 8,
            EmotionalPower = 7,
            Conflict = 9,
            Character = 8,
            Visual = 6,
            OriginalCombination = 7,
            NovelLength = 8,
            DifficultChoices = 7,
            AudienceFit = 8,
            SeriesFit = 7,
        });

        Assert.NotNull(scored.Score);
        Assert.Equal(75, scored.Score!.Total);
        Assert.Equal("Pursue", scored.Score.Decision);

        var loaded = await _ideas.GetAsync(project.Id, created.Id);
        Assert.Equal(75, loaded.Score!.Total);

        await _ideas.DeleteAsync(project.Id, created.Id);
        Assert.Empty(await _ideas.GetAllAsync(project.Id));
    }

    [Fact]
    public async Task PublishingRouteChange_PreservesInactiveBranchProgress()
    {
        var project = await CreateAsync("Routes");
        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.Traditional);
        var traditionalPhases = await _workflow.GetApplicablePhasesAsync(project.Id);
        var traditionalOnly = traditionalPhases.FirstOrDefault(phase =>
            phase.RouteAffinity == PublishingRoute.Traditional);
        Assert.NotNull(traditionalOnly);

        var step = traditionalOnly!.Steps.First();
        await _workflow.CompleteStepAsync(project.Id, traditionalOnly.Id, step.Number);

        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.SelfPublishing);
        var selfPhases = await _workflow.GetApplicablePhasesAsync(project.Id);
        Assert.DoesNotContain(selfPhases, phase => phase.Id == traditionalOnly.Id);

        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.Traditional);
        var progress = await _workflow.GetPhaseStepProgressAsync(project.Id, traditionalOnly.Id);
        Assert.Contains(progress, item => item.StepNumber == step.Number && item.Status == StepStatus.Complete);
    }

    [Fact]
    public async Task ToolsReportExport_ProtectsOverwrite()
    {
        var project = await CreateAsync("Export");
        var path = await _exporter.ExportMarkdownAsync(project.Id, "tools-report.md", "# Hello\n");
        Assert.Equal("Exports/tools-report.md", path.Replace('\\', '/'));
        Assert.True(File.Exists(Path.Combine(project.RootPath, "Exports", "tools-report.md")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _exporter.ExportMarkdownAsync(project.Id, "tools-report.md", "# Again\n"));
    }

    [Fact]
    public async Task NewProject_UsesSchemaVersion5()
    {
        var project = await CreateAsync("Schema5");
        var validation = await _projects.ValidateAsync(project.RootPath);
        Assert.Equal(7, validation.SchemaVersion);
        Assert.Equal(ProjectSchema.CurrentVersion, validation.SchemaVersion);
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
