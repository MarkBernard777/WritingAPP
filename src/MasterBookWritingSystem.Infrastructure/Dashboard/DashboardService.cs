using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Dashboard;
using MasterBookWritingSystem.Core.Domain.Workflow;
using MasterBookWritingSystem.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Dashboard;

public sealed class DashboardService : IDashboardService
{
    private readonly IProjectService _projectService;
    private readonly IWorkflowService _workflowService;

    public DashboardService(IProjectService projectService, IWorkflowService workflowService)
    {
        _projectService = projectService;
        _workflowService = workflowService;
    }

    public async Task<ProjectDashboardSummary?> BuildAsync(CancellationToken cancellationToken = default)
    {
        var project = _projectService.ActiveProject;
        if (project is null)
        {
            return null;
        }

        var phases = await _workflowService
            .GetApplicablePhasesAsync(project.Id, cancellationToken)
            .ConfigureAwait(false);

        var databasePath = Path.Combine(project.RootPath, ProjectPaths.DatabaseFileName);
        List<StepProgressRecordSnapshot> completedSteps;
        List<string> passedGates;
        int wordCount;

        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            completedSteps = await context.StepProgress
                .AsNoTracking()
                .Where(item => item.ProjectId == project.Id && item.Status == StepStatus.Complete)
                .Select(item => new StepProgressRecordSnapshot(item.PhaseId, item.StepNumber))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            passedGates = await context.PhaseGates
                .AsNoTracking()
                .Where(item => item.ProjectId == project.Id && item.IsPassed)
                .Select(item => item.PhaseId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            wordCount = await context.Chapters
                .AsNoTracking()
                .Where(item => item.ProjectId == project.Id)
                .SumAsync(item => (int?)item.WordCount ?? 0, cancellationToken)
                .ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();

        var applicablePhaseIds = phases.Select(phase => phase.Id).ToHashSet(StringComparer.Ordinal);
        var completedApplicable = completedSteps
            .Count(item => applicablePhaseIds.Contains(item.PhaseId));
        var passedApplicable = passedGates.Count(phaseId => applicablePhaseIds.Contains(phaseId));

        var currentPhase = phases.FirstOrDefault(phase => !passedGates.Contains(phase.Id));
        string nextAction;
        if (currentPhase is null)
        {
            nextAction = "All applicable phase gates are complete.";
        }
        else
        {
            var completedNumbers = completedSteps
                .Where(item => item.PhaseId == currentPhase.Id)
                .Select(item => item.StepNumber)
                .ToHashSet();

            var nextStep = currentPhase.Steps.FirstOrDefault(step => !completedNumbers.Contains(step.Number));
            nextAction = nextStep is null
                ? $"Pass the gate for phase {currentPhase.Id}: {currentPhase.Title}"
                : $"Complete step {nextStep.Number}: {nextStep.Title}";
        }

        return new ProjectDashboardSummary
        {
            ProjectId = project.Id,
            Title = project.Title,
            Author = project.Author,
            Genre = project.Genre,
            PublishingRoute = project.PublishingRoute,
            NorthStar = project.NorthStar,
            RootPath = project.RootPath,
            ApplicablePhaseCount = phases.Count,
            ApplicableStepCount = phases.Sum(phase => phase.Steps.Count),
            CompletedStepCount = completedApplicable,
            PassedGateCount = passedApplicable,
            CurrentPhaseId = currentPhase?.Id,
            CurrentPhaseTitle = currentPhase?.Title,
            CurrentDeliverable = currentPhase?.Deliverable ?? string.Empty,
            NextAction = nextAction,
            WordCount = wordCount,
            LastEditedUtc = project.LastEditedUtc,
        };
    }

    private sealed record StepProgressRecordSnapshot(string PhaseId, int StepNumber);
}
