using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Documents;
using MasterBookWritingSystem.Core.Domain.Workflow;
using MasterBookWritingSystem.Core.Workflow;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Workflow;

public sealed class WorkflowSqliteIntegrationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _workflowPath;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IWorkflowService _workflow;

    public WorkflowSqliteIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-workflow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _workflowPath = FindWorkflowSeedPath();

        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(new FileWorkflowDefinitionSource(_workflowPath));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _workflow = _provider.GetRequiredService<IWorkflowService>();
    }

    public void Dispose()
    {
        _projects.CloseAsync().GetAwaiter().GetResult();
        _provider.Dispose();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Import_Writes_23Phases_And_112Steps()
    {
        var project = await CreateProjectAsync("Import Counts");

        var phases = await _workflow.GetAllPhasesAsync(project.Id);
        var steps = phases.SelectMany(phase => phase.Steps).ToList();

        Assert.Equal(23, phases.Count);
        Assert.Equal(112, steps.Count);
        Assert.Contains(phases, phase => phase.Id == "20A");
        Assert.Contains(phases, phase => phase.Id == "20B");
    }

    [Theory]
    [InlineData(PublishingRoute.SelfPublishing, 22, 107)]
    [InlineData(PublishingRoute.Traditional, 22, 104)]
    [InlineData(PublishingRoute.Unspecified, 23, 112)]
    [InlineData(PublishingRoute.Hybrid, 23, 112)]
    public async Task ApplicableWorkflow_MatchesRouteCounts(
        PublishingRoute route,
        int expectedPhases,
        int expectedSteps)
    {
        var project = await CreateProjectAsync($"Route {route}");
        await _workflow.SetPublishingRouteAsync(project.Id, route);

        var applicable = await _workflow.GetApplicablePhasesAsync(project.Id);

        Assert.Equal(expectedPhases, applicable.Count);
        Assert.Equal(expectedSteps, applicable.Sum(phase => phase.Steps.Count));
    }

    [Fact]
    public async Task ChangingRoute_NeverDeletesInactiveBranchProgress()
    {
        var project = await CreateProjectAsync("Preserve Branch");
        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.Traditional);

        await _workflow.CompleteStepAsync(project.Id, "20A", stepNumber: 91);
        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.SelfPublishing);

        var allPhases = await _workflow.GetAllPhasesAsync(project.Id);
        var traditionalProgress = await _workflow.GetStepProgressAsync(project.Id, "20A", 91);

        Assert.Contains(allPhases, phase => phase.Id == "20A");
        Assert.Contains(allPhases, phase => phase.Id == "20B");
        Assert.Equal(StepStatus.Complete, traditionalProgress.Status);

        var applicable = await _workflow.GetApplicablePhasesAsync(project.Id);
        Assert.DoesNotContain(applicable, phase => phase.Id == "20A");
        Assert.Contains(applicable, phase => phase.Id == "20B");
    }

    [Fact]
    public async Task Gate_IsDisabledUntilStepsComplete_ThenStoresEvidence()
    {
        var project = await CreateProjectAsync("Gate Rules");
        var phase = (await _workflow.GetAllPhasesAsync(project.Id)).Single(item => item.Id == "4");
        var documents = _provider.GetRequiredService<IDocumentService>();

        Assert.False(await _workflow.CanPassGateAsync(project.Id, phase.Id));

        foreach (var step in phase.Steps)
        {
            await _workflow.CompleteStepAsync(project.Id, phase.Id, step.Number);
        }

        Assert.False(await _workflow.CanPassGateAsync(project.Id, phase.Id));

        var themeMap = await documents.GetAsync(project.Id, DocumentType.ThemeMap);
        foreach (var field in themeMap.Fields.Where(item => item.IsRequired))
        {
            await documents.UpdateFieldAsync(project.Id, themeMap.Id, field.Key, "Ready");
        }

        Assert.True(await _workflow.CanPassGateAsync(project.Id, phase.Id));

        var passed = await _workflow.PassGateAsync(project.Id, new PhaseGateCompletion
        {
            PhaseId = phase.Id,
            ConfirmedByUser = true,
            Evidence = "Theme Map complete",
            Notes = "Theme drives antagonist choices",
            PassedUtc = DateTimeOffset.UtcNow,
        });

        Assert.True(passed.IsPassed);
        Assert.True(passed.ConfirmedByUser);
        Assert.Equal("Theme Map complete", passed.Evidence);
        Assert.Equal("Theme drives antagonist choices", passed.Notes);
        Assert.NotNull(passed.PassedUtc);

        await using var db = ProjectDbContextFactory.Create(
            Path.Combine(project.RootPath, ProjectPaths.DatabaseFileName));
        var stored = await db.PhaseGates.SingleAsync(gate => gate.ProjectId == project.Id && gate.PhaseId == phase.Id);
        Assert.True(stored.IsPassed);
        Assert.Equal("Theme Map complete", stored.Evidence);
        Assert.NotNull(stored.PassedUtc);
    }

    private async Task<Project> CreateProjectAsync(string title)
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);
        return await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = title,
            Author = "Tester",
        });
    }

    private static string FindWorkflowSeedPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "seed", "workflow.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate seed/workflow.json.");
    }
}
