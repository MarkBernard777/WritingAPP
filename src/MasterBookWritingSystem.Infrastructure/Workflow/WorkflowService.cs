using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Workflow;
using MasterBookWritingSystem.Core.Workflow;
using MasterBookWritingSystem.Infrastructure.Persistence;
using MasterBookWritingSystem.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MasterBookWritingSystem.Infrastructure.Workflow;

public sealed class WorkflowService : IWorkflowService
{
    private readonly IProjectService _projectService;
    private readonly IWorkflowDefinitionSource _definitionSource;

    public WorkflowService(IProjectService projectService, IWorkflowDefinitionSource definitionSource)
    {
        _projectService = projectService;
        _definitionSource = definitionSource;
    }

    public async Task ImportDefinitionsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var definitions = await _definitionSource.LoadAsync(cancellationToken).ConfigureAwait(false);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            await WorkflowDefinitionImporter
                .ImportAsync(context, projectId, definitions, cancellationToken)
                .ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();
    }

    public async Task<IReadOnlyList<WorkflowPhase>> GetAllPhasesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using var context = ProjectDbContextFactory.Create(databasePath);
        var records = await context.WorkflowPhases
            .AsNoTracking()
            .Include(phase => phase.Steps)
            .Where(phase => phase.ProjectId == projectId)
            .OrderBy(phase => phase.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        SqliteConnection.ClearAllPools();
        return records.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<WorkflowPhase>> GetApplicablePhasesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await GetProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var all = await GetAllPhasesAsync(projectId, cancellationToken).ConfigureAwait(false);
        return WorkflowRouteFilter.Filter(all, project.PublishingRoute);
    }

    public async Task SetPublishingRouteAsync(
        Guid projectId,
        PublishingRoute route,
        CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            var project = await context.Projects
                .FirstOrDefaultAsync(item => item.Id == projectId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ProjectNotFoundException($"Project '{projectId}' was not found.");

            // Route changes only affect applicability filtering — never delete branch rows.
            project.PublishingRoute = route;
            project.LastEditedUtc = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();

        if (_projectService.ActiveProject?.Id == projectId)
        {
            _projectService.ActiveProject.PublishingRoute = route;
            _projectService.ActiveProject.LastEditedUtc = DateTimeOffset.UtcNow;
        }
    }

    public async Task CompleteStepAsync(
        Guid projectId,
        string phaseId,
        int stepNumber,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            var stepExists = await context.WorkflowSteps.AnyAsync(
                    step => step.ProjectId == projectId
                        && step.PhaseId == phaseId
                        && step.Number == stepNumber,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!stepExists)
            {
                throw new InvalidOperationException($"Step {stepNumber} was not found in phase '{phaseId}'.");
            }

            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var progress = await context.StepProgress.FirstOrDefaultAsync(
                    item => item.ProjectId == projectId
                        && item.PhaseId == phaseId
                        && item.StepNumber == stepNumber,
                    cancellationToken)
                .ConfigureAwait(false);

            if (progress is null)
            {
                progress = new StepProgressRecord
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    PhaseId = phaseId,
                    StepNumber = stepNumber,
                };
                context.StepProgress.Add(progress);
            }

            progress.Status = StepStatus.Complete;
            progress.CompletedUtc = DateTimeOffset.UtcNow;
            if (notes is not null)
            {
                progress.Notes = notes;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();
    }

    public async Task<StepProgress> GetStepProgressAsync(
        Guid projectId,
        string phaseId,
        int stepNumber,
        CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using var context = ProjectDbContextFactory.Create(databasePath);
        var progress = await context.StepProgress
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId
                    && item.PhaseId == phaseId
                    && item.StepNumber == stepNumber,
                cancellationToken)
            .ConfigureAwait(false);

        SqliteConnection.ClearAllPools();

        if (progress is null)
        {
            return new StepProgress
            {
                Id = Guid.Empty,
                ProjectId = projectId,
                PhaseId = phaseId,
                StepNumber = stepNumber,
                Status = StepStatus.NotStarted,
            };
        }

        return ToDomainProgress(progress);
    }

    public async Task<IReadOnlyList<StepProgress>> GetPhaseStepProgressAsync(
        Guid projectId,
        string phaseId,
        CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using var context = ProjectDbContextFactory.Create(databasePath);
        var progress = await context.StepProgress
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.PhaseId == phaseId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        SqliteConnection.ClearAllPools();
        return progress.Select(ToDomainProgress).ToList();
    }

    public async Task<PhaseGate?> GetPhaseGateAsync(
        Guid projectId,
        string phaseId,
        CancellationToken cancellationToken = default)
    {
        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using var context = ProjectDbContextFactory.Create(databasePath);
        var gate = await context.PhaseGates
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.ProjectId == projectId && item.PhaseId == phaseId,
                cancellationToken)
            .ConfigureAwait(false);

        SqliteConnection.ClearAllPools();
        return gate is null ? null : ToDomainGate(gate);
    }

    public async Task<bool> CanPassGateAsync(
        Guid projectId,
        string phaseId,
        CancellationToken cancellationToken = default)
    {
        var phases = await GetAllPhasesAsync(projectId, cancellationToken).ConfigureAwait(false);
        var phase = phases.SingleOrDefault(item => item.Id == phaseId)
            ?? throw new InvalidOperationException($"Phase '{phaseId}' was not found.");

        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using var context = ProjectDbContextFactory.Create(databasePath);
        var progress = await context.StepProgress
            .AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.PhaseId == phaseId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        SqliteConnection.ClearAllPools();

        var domainProgress = progress.Select(item => new StepProgress
        {
            Id = item.Id,
            ProjectId = item.ProjectId,
            PhaseId = item.PhaseId,
            StepNumber = item.StepNumber,
            Status = item.Status,
            Notes = item.Notes,
            CompletedUtc = item.CompletedUtc,
        });

        return PhaseGateRules.CanPass(phase, domainProgress);
    }

    public async Task<PhaseGate> PassGateAsync(
        Guid projectId,
        PhaseGateCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        if (!completion.ConfirmedByUser && string.IsNullOrWhiteSpace(completion.OverrideReason))
        {
            throw new InvalidOperationException("Gate confirmation is required unless an override reason is provided.");
        }

        if (!await CanPassGateAsync(projectId, completion.PhaseId, cancellationToken).ConfigureAwait(false)
            && string.IsNullOrWhiteSpace(completion.OverrideReason))
        {
            throw new InvalidOperationException(
                "Gate is disabled until every required step is complete.");
        }

        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);
        PhaseGateRecord gate;

        await using (var context = ProjectDbContextFactory.Create(databasePath))
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var existing = await context.PhaseGates.FirstOrDefaultAsync(
                    item => item.ProjectId == projectId && item.PhaseId == completion.PhaseId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                gate = new PhaseGateRecord
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    PhaseId = completion.PhaseId,
                };
                context.PhaseGates.Add(gate);
            }
            else
            {
                gate = existing;
            }

            gate.IsPassed = true;
            gate.ConfirmedByUser = completion.ConfirmedByUser;
            gate.Evidence = completion.Evidence;
            gate.Notes = completion.Notes;
            gate.OverrideReason = completion.OverrideReason;
            gate.PassedUtc = completion.PassedUtc ?? DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        SqliteConnection.ClearAllPools();

        return ToDomainGate(gate);
    }

    private string RequireActiveRoot(Guid projectId)
    {
        var active = _projectService.ActiveProject
            ?? throw new InvalidOperationException("Open a project before using workflow services.");

        if (active.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return active.RootPath;
    }

    private async Task<ProjectRecord> GetProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var rootPath = RequireActiveRoot(projectId);
        var databasePath = Path.Combine(rootPath, ProjectPaths.DatabaseFileName);

        await using var context = ProjectDbContextFactory.Create(databasePath);
        var project = await context.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == projectId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProjectNotFoundException($"Project '{projectId}' was not found.");

        SqliteConnection.ClearAllPools();
        return project;
    }

    private static WorkflowPhase ToDomain(WorkflowPhaseRecord record)
    {
        var phase = new WorkflowPhase
        {
            Id = record.Id,
            Title = record.Title,
            Deliverable = record.Deliverable,
            GateStatement = record.GateStatement,
            TemplatePath = record.TemplatePath,
            RouteAffinity = record.RouteAffinity,
        };

        foreach (var step in record.Steps.OrderBy(item => item.Number))
        {
            phase.Steps.Add(new WorkflowStep
            {
                Number = step.Number,
                Title = step.Title,
                PhaseId = step.PhaseId,
                RouteAffinity = step.RouteAffinity,
            });
        }

        return phase;
    }

    private static StepProgress ToDomainProgress(StepProgressRecord progress) => new()
    {
        Id = progress.Id,
        ProjectId = progress.ProjectId,
        PhaseId = progress.PhaseId,
        StepNumber = progress.StepNumber,
        Status = progress.Status,
        Notes = progress.Notes,
        CompletedUtc = progress.CompletedUtc,
    };

    private static PhaseGate ToDomainGate(PhaseGateRecord gate) => new()
    {
        Id = gate.Id,
        ProjectId = gate.ProjectId,
        PhaseId = gate.PhaseId,
        IsPassed = gate.IsPassed,
        ConfirmedByUser = gate.ConfirmedByUser,
        Evidence = gate.Evidence,
        Notes = gate.Notes,
        OverrideReason = gate.OverrideReason,
        PassedUtc = gate.PassedUtc,
    };
}

public static class WorkflowDefinitionImporter
{
    public static async Task ImportAsync(
        ProjectDbContext context,
        Guid projectId,
        IReadOnlyList<WorkflowPhase> definitions,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.WorkflowPhases
            .CountAsync(phase => phase.ProjectId == projectId, cancellationToken)
            .ConfigureAwait(false);
        if (existing > 0)
        {
            return;
        }

        var sortOrder = 0;
        foreach (var phase in definitions)
        {
            context.WorkflowPhases.Add(new WorkflowPhaseRecord
            {
                Id = phase.Id,
                ProjectId = projectId,
                Title = phase.Title,
                Deliverable = phase.Deliverable,
                GateStatement = phase.GateStatement,
                TemplatePath = phase.TemplatePath,
                RouteAffinity = phase.RouteAffinity,
                SortOrder = sortOrder++,
            });

            foreach (var step in phase.Steps)
            {
                context.WorkflowSteps.Add(new WorkflowStepRecord
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    PhaseId = phase.Id,
                    Number = step.Number,
                    Title = step.Title,
                    ContentJson = "[]",
                    RouteAffinity = step.RouteAffinity,
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
