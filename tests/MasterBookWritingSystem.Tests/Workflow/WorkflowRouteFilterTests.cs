using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Workflow;
using MasterBookWritingSystem.Core.Workflow;

namespace MasterBookWritingSystem.Tests.Workflow;

public class WorkflowRouteFilterTests
{
    [Theory]
    [InlineData(PublishingRoute.SelfPublishing, 22, 107)]
    [InlineData(PublishingRoute.Traditional, 22, 104)]
    [InlineData(PublishingRoute.Unspecified, 23, 112)]
    [InlineData(PublishingRoute.Hybrid, 23, 112)]
    public void Filter_ReturnsExpectedPhaseAndStepCounts(
        PublishingRoute route,
        int expectedPhases,
        int expectedSteps)
    {
        var catalog = CreateCatalog();

        var applicable = WorkflowRouteFilter.Filter(catalog, route);

        Assert.Equal(expectedPhases, applicable.Count);
        Assert.Equal(expectedSteps, applicable.Sum(phase => phase.Steps.Count));
    }

    [Fact]
    public void SelfPublishing_HidesTraditionalBranch_ButKeepsSelfPublishingBranch()
    {
        var applicable = WorkflowRouteFilter.Filter(CreateCatalog(), PublishingRoute.SelfPublishing);

        Assert.DoesNotContain(applicable, phase => phase.Id == "20A");
        Assert.Contains(applicable, phase => phase.Id == "20B");
    }

    [Fact]
    public void Traditional_HidesSelfPublishingBranch_ButKeepsTraditionalBranch()
    {
        var applicable = WorkflowRouteFilter.Filter(CreateCatalog(), PublishingRoute.Traditional);

        Assert.Contains(applicable, phase => phase.Id == "20A");
        Assert.DoesNotContain(applicable, phase => phase.Id == "20B");
    }

    [Theory]
    [InlineData(PublishingRoute.Unspecified)]
    [InlineData(PublishingRoute.Hybrid)]
    public void UndecidedAndHybrid_PreserveBothPhase20Branches(PublishingRoute route)
    {
        var applicable = WorkflowRouteFilter.Filter(CreateCatalog(), route);

        Assert.Contains(applicable, phase => phase.Id == "20A");
        Assert.Contains(applicable, phase => phase.Id == "20B");
    }

    private static IReadOnlyList<WorkflowPhase> CreateCatalog() =>
    [
        CreatePhase("1", 3, null),
        CreatePhase("2", 4, null),
        CreatePhase("3", 4, null),
        CreatePhase("4", 3, null),
        CreatePhase("5", 5, null),
        CreatePhase("6", 9, null),
        CreatePhase("7", 7, null),
        CreatePhase("8", 7, null),
        CreatePhase("9", 6, null),
        CreatePhase("10", 5, null),
        CreatePhase("11", 3, null),
        CreatePhase("12", 6, null),
        CreatePhase("13", 4, null),
        CreatePhase("14", 6, null),
        CreatePhase("15", 4, null),
        CreatePhase("16", 4, null),
        CreatePhase("17", 4, null),
        CreatePhase("18", 4, null),
        CreatePhase("19", 2, null),
        CreatePhase("20A", 5, PublishingRoute.Traditional),
        CreatePhase("20B", 8, PublishingRoute.SelfPublishing),
        CreatePhase("21", 5, null),
        CreatePhase("22", 4, null),
    ];

    private static WorkflowPhase CreatePhase(string id, int stepCount, PublishingRoute? affinity)
    {
        var phase = new WorkflowPhase
        {
            Id = id,
            Title = $"Phase {id}",
            GateStatement = $"Gate {id}",
            RouteAffinity = affinity,
        };

        for (var number = 1; number <= stepCount; number++)
        {
            phase.Steps.Add(new WorkflowStep
            {
                Number = number,
                Title = $"Step {number}",
                PhaseId = id,
                RouteAffinity = affinity,
            });
        }

        return phase;
    }
}
