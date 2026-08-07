using MasterBookWritingSystem.Core.Domain.Workflow;
using MasterBookWritingSystem.Core.Workflow;

namespace MasterBookWritingSystem.Tests.Workflow;

public class PhaseGateRulesTests
{
    [Fact]
    public void Gate_IsDisabled_UntilEveryRequiredStepIsComplete()
    {
        var phase = new WorkflowPhase
        {
            Id = "1",
            Title = "Define",
            GateStatement = "Explain the book.",
            Steps =
            [
                new WorkflowStep { Number = 1, Title = "Purpose", PhaseId = "1" },
                new WorkflowStep { Number = 2, Title = "Category", PhaseId = "1" },
            ],
        };

        var progress = new List<StepProgress>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                PhaseId = "1",
                StepNumber = 1,
                Status = StepStatus.Complete,
            },
            new()
            {
                Id = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                PhaseId = "1",
                StepNumber = 2,
                Status = StepStatus.InProgress,
            },
        };

        Assert.False(PhaseGateRules.CanPass(phase, progress));

        progress[1].Status = StepStatus.Complete;
        Assert.True(PhaseGateRules.CanPass(phase, progress));
    }

    [Fact]
    public void GateCompletion_RequiresDateEvidenceAndOptionalNotes()
    {
        var completion = new PhaseGateCompletion
        {
            PhaseId = "1",
            ConfirmedByUser = true,
            Evidence = "Project Definition draft attached",
            Notes = "Ready for plotting",
            PassedUtc = DateTimeOffset.Parse("2026-08-07T10:00:00Z"),
        };

        Assert.True(completion.ConfirmedByUser);
        Assert.False(string.IsNullOrWhiteSpace(completion.Evidence));
        Assert.Equal("Ready for plotting", completion.Notes);
        Assert.NotNull(completion.PassedUtc);
    }
}
