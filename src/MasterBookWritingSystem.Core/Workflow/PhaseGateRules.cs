using MasterBookWritingSystem.Core.Domain.Workflow;

namespace MasterBookWritingSystem.Core.Workflow;

public static class PhaseGateRules
{
    public static bool CanPass(WorkflowPhase phase, IEnumerable<StepProgress> progressRecords)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(progressRecords);

        if (phase.Steps.Count == 0)
        {
            return false;
        }

        var completed = progressRecords
            .Where(progress => progress.PhaseId == phase.Id && progress.Status == StepStatus.Complete)
            .Select(progress => progress.StepNumber)
            .ToHashSet();

        return phase.Steps.All(step => completed.Contains(step.Number));
    }
}
