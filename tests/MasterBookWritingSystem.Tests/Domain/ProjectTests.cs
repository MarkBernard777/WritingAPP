using MasterBookWritingSystem.Core.Domain;
using MasterBookWritingSystem.Core.Domain.Workflow;

namespace MasterBookWritingSystem.Tests.Domain;

public class ProjectTests
{
    [Fact]
    public void Project_UsesStableGuidIdentity()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var project = new Project
        {
            Id = id,
            Title = "Oath of Embers",
            Author = "A. Writer",
            Genre = "Epic Fantasy",
            PublishingRoute = PublishingRoute.SelfPublishing,
            NorthStar = "A hopeful epic about broken oaths.",
            RootPath = @"D:\Books\OathOfEmbers",
            CreatedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            LastEditedUtc = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
        };

        Assert.Equal(id, project.Id);
        Assert.Equal("Oath of Embers", project.Title);
        Assert.Equal(PublishingRoute.SelfPublishing, project.PublishingRoute);
        Assert.False(string.IsNullOrWhiteSpace(project.RootPath));
    }

    [Fact]
    public void PublishingRoute_SupportsBothBranches()
    {
        Assert.Equal(0, (int)PublishingRoute.Unspecified);
        Assert.Equal(1, (int)PublishingRoute.SelfPublishing);
        Assert.Equal(2, (int)PublishingRoute.Traditional);
    }

    [Fact]
    public void WorkflowPhase_HoldsGateAndSteps()
    {
        var phase = new WorkflowPhase
        {
            Id = "1",
            Title = "Define the Book You Are Creating",
            Deliverable = "complete Project Definition",
            GateStatement = "Do not begin detailed plotting until you can explain the intended book clearly.",
            TemplatePath = "templates/core/01_Project_Definition.md",
            Steps =
            [
                new WorkflowStep
                {
                    Number = 1,
                    Title = "Decide the project’s purpose",
                    PhaseId = "1",
                },
            ],
        };

        Assert.Equal("1", phase.Id);
        Assert.Single(phase.Steps);
        Assert.Equal(1, phase.Steps[0].Number);
        Assert.False(string.IsNullOrWhiteSpace(phase.GateStatement));
    }

    [Fact]
    public void StepProgress_DefaultsToNotStarted()
    {
        var progress = new StepProgress
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            PhaseId = "1",
            StepNumber = 1,
        };

        Assert.Equal(StepStatus.NotStarted, progress.Status);
        Assert.Null(progress.CompletedUtc);
    }

    [Fact]
    public void PhaseGate_CannotPassWithoutConfirmation()
    {
        var gate = new PhaseGate
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            PhaseId = "1",
            IsPassed = false,
            ConfirmedByUser = false,
        };

        Assert.False(gate.IsPassed);
        Assert.False(gate.ConfirmedByUser);
        Assert.Null(gate.OverrideReason);
    }
}
