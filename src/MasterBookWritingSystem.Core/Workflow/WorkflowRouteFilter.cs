using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Workflow;

namespace MasterBookWritingSystem.Core.Workflow;

public static class WorkflowRouteFilter
{
    public static IReadOnlyList<WorkflowPhase> Filter(
        IEnumerable<WorkflowPhase> phases,
        PublishingRoute route)
    {
        ArgumentNullException.ThrowIfNull(phases);

        return phases
            .Where(phase => IsPhaseApplicable(phase, route))
            .Select(phase => ClonePhase(phase, route))
            .ToList();
    }

    public static bool IsPhaseApplicable(WorkflowPhase phase, PublishingRoute route)
    {
        ArgumentNullException.ThrowIfNull(phase);

        return route switch
        {
            PublishingRoute.SelfPublishing => phase.RouteAffinity is null or PublishingRoute.SelfPublishing,
            PublishingRoute.Traditional => phase.RouteAffinity is null or PublishingRoute.Traditional,
            PublishingRoute.Unspecified or PublishingRoute.Hybrid => true,
            _ => true,
        };
    }

    private static WorkflowPhase ClonePhase(WorkflowPhase phase, PublishingRoute route)
    {
        var clone = new WorkflowPhase
        {
            Id = phase.Id,
            Title = phase.Title,
            Deliverable = phase.Deliverable,
            GateStatement = phase.GateStatement,
            TemplatePath = phase.TemplatePath,
            RouteAffinity = phase.RouteAffinity,
        };

        foreach (var step in phase.Steps.Where(step => IsStepApplicable(step, route)))
        {
            clone.Steps.Add(step);
        }

        return clone;
    }

    private static bool IsStepApplicable(WorkflowStep step, PublishingRoute route)
        => route switch
        {
            PublishingRoute.SelfPublishing => step.RouteAffinity is null or PublishingRoute.SelfPublishing,
            PublishingRoute.Traditional => step.RouteAffinity is null or PublishingRoute.Traditional,
            _ => true,
        };
}
