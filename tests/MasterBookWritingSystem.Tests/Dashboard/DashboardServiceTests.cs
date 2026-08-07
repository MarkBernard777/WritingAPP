using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Infrastructure.DependencyInjection;
using MasterBookWritingSystem.Infrastructure.Workflow;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace MasterBookWritingSystem.Tests.Dashboard;

public sealed class DashboardServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServiceProvider _provider;
    private readonly IProjectService _projects;
    private readonly IWorkflowService _workflow;
    private readonly IDashboardService _dashboard;

    public DashboardServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "mbws-dashboard-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var workflowPath = FindWorkflowSeedPath();
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton<IWorkflowDefinitionSource>(new FileWorkflowDefinitionSource(workflowPath));
        _provider = services.BuildServiceProvider();
        _projects = _provider.GetRequiredService<IProjectService>();
        _workflow = _provider.GetRequiredService<IWorkflowService>();
        _dashboard = _provider.GetRequiredService<IDashboardService>();
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
    public async Task Build_ReturnsNull_WhenNoProjectOpen()
    {
        var summary = await _dashboard.BuildAsync();
        Assert.Null(summary);
    }

    [Fact]
    public async Task Build_IncludesProjectIdentityAndWorkflowCounts()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);
        var project = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Dashboard Book",
            Author = "A. Writer",
            Genre = "Fantasy",
            NorthStar = "Hope through cost",
        });

        await _workflow.SetPublishingRouteAsync(project.Id, PublishingRoute.SelfPublishing);
        await _workflow.CompleteStepAsync(project.Id, "1", 1);

        var summary = await _dashboard.BuildAsync();

        Assert.NotNull(summary);
        Assert.Equal("Dashboard Book", summary!.Title);
        Assert.Equal("A. Writer", summary.Author);
        Assert.Equal("Fantasy", summary.Genre);
        Assert.Equal(PublishingRoute.SelfPublishing, summary.PublishingRoute);
        Assert.Equal("Hope through cost", summary.NorthStar);
        Assert.Equal(22, summary.ApplicablePhaseCount);
        Assert.Equal(107, summary.ApplicableStepCount);
        Assert.Equal(1, summary.CompletedStepCount);
        Assert.Equal(0, summary.PassedGateCount);
        Assert.Equal("1", summary.CurrentPhaseId);
        Assert.Equal("Define the Book You Are Creating", summary.CurrentPhaseTitle);
        Assert.False(string.IsNullOrWhiteSpace(summary.CurrentDeliverable));
        Assert.Contains("Define the broad category", summary.NextAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Build_AdvancesCurrentPhase_AfterGatePass()
    {
        var parent = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(parent);
        var project = await _projects.CreateAsync(new CreateProjectRequest
        {
            ParentDirectory = parent,
            Title = "Phase Advance",
            Author = "A. Writer",
        });

        var phase = (await _workflow.GetApplicablePhasesAsync(project.Id)).Single(item => item.Id == "1");
        foreach (var step in phase.Steps)
        {
            await _workflow.CompleteStepAsync(project.Id, phase.Id, step.Number);
        }

        await _workflow.PassGateAsync(project.Id, new Core.Workflow.PhaseGateCompletion
        {
            PhaseId = phase.Id,
            ConfirmedByUser = true,
            Evidence = "Project Definition ready",
            Notes = "Clear in under a minute",
            PassedUtc = DateTimeOffset.UtcNow,
        });

        var summary = await _dashboard.BuildAsync();

        Assert.NotNull(summary);
        Assert.Equal(1, summary!.PassedGateCount);
        Assert.Equal("2", summary.CurrentPhaseId);
        Assert.Equal(phase.Steps.Count, summary.CompletedStepCount);
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
