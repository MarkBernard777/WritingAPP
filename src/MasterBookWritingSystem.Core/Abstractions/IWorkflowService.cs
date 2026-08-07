using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Workflow;
using MasterBookWritingSystem.Core.Workflow;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IWorkflowDefinitionSource
{
    Task<IReadOnlyList<WorkflowPhase>> LoadAsync(CancellationToken cancellationToken = default);
}

public interface IWorkflowService
{
    Task ImportDefinitionsAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowPhase>> GetAllPhasesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowPhase>> GetApplicablePhasesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task SetPublishingRouteAsync(
        Guid projectId,
        PublishingRoute route,
        CancellationToken cancellationToken = default);

    Task CompleteStepAsync(
        Guid projectId,
        string phaseId,
        int stepNumber,
        CancellationToken cancellationToken = default);

    Task<StepProgress> GetStepProgressAsync(
        Guid projectId,
        string phaseId,
        int stepNumber,
        CancellationToken cancellationToken = default);

    Task<bool> CanPassGateAsync(
        Guid projectId,
        string phaseId,
        CancellationToken cancellationToken = default);

    Task<PhaseGate> PassGateAsync(
        Guid projectId,
        PhaseGateCompletion completion,
        CancellationToken cancellationToken = default);
}
