using MasterBookWritingSystem.Core.Domain.Documents;
using MasterBookWritingSystem.Core.Domain.Workflow;

namespace MasterBookWritingSystem.Core.Workflow;

public static class PhaseGateRules
{
    public static bool CanPass(WorkflowPhase phase, IEnumerable<StepProgress> progressRecords)
        => CanPass(phase, progressRecords, requiredDocument: null, requireDocumentWhenMapped: false);

    public static bool CanPass(
        WorkflowPhase phase,
        IEnumerable<StepProgress> progressRecords,
        WorkingDocument? requiredDocument,
        bool requireDocumentWhenMapped = true)
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

        if (!phase.Steps.All(step => completed.Contains(step.Number)))
        {
            return false;
        }

        if (!requireDocumentWhenMapped)
        {
            return true;
        }

        // Extra templates (non-core) do not map to the 24 documents.
        if (requiredDocument is null)
        {
            return string.IsNullOrWhiteSpace(phase.TemplatePath)
                || !phase.TemplatePath.Contains("templates/core/", StringComparison.OrdinalIgnoreCase);
        }

        return requiredDocument.Fields
            .Where(field => field.IsRequired)
            .All(field => !string.IsNullOrWhiteSpace(field.Value));
    }

    public static int CalculateCompletionPercentage(IEnumerable<DocumentField> fields)
    {
        var required = fields.Where(field => field.IsRequired).ToList();
        if (required.Count == 0)
        {
            return 100;
        }

        var filled = required.Count(field => !string.IsNullOrWhiteSpace(field.Value));
        return (int)Math.Round(filled * 100d / required.Count);
    }
}
