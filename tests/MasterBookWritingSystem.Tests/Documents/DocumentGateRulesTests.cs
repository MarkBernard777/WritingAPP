using MasterBookWritingSystem.Core.Domain.Documents;
using MasterBookWritingSystem.Core.Domain.Workflow;
using MasterBookWritingSystem.Core.Workflow;

namespace MasterBookWritingSystem.Tests.Documents;

public class DocumentGateRulesTests
{
    [Fact]
    public void Gate_Fails_WhenRequiredDocumentFieldsMissing()
    {
        var phase = new WorkflowPhase
        {
            Id = "1",
            Title = "Define",
            TemplatePath = "templates/core/01_Project_Definition.md",
            Steps = [new WorkflowStep { Number = 1, Title = "Purpose", PhaseId = "1" }],
        };
        var progress = new[]
        {
            new StepProgress
            {
                Id = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                PhaseId = "1",
                StepNumber = 1,
                Status = StepStatus.Complete,
            },
        };
        var document = new WorkingDocument
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            DocumentType = DocumentType.ProjectDefinition,
            Title = "Project Definition",
            RelativeMarkdownPath = "01_Project_Definition/01_Project_Definition.md",
            Fields =
            [
                new DocumentField { Key = "WHY", Label = "Why", IsRequired = true, Value = "" },
            ],
        };

        Assert.False(PhaseGateRules.CanPass(phase, progress, document));
    }

    [Fact]
    public void Gate_Passes_WhenStepsAndRequiredFieldsComplete()
    {
        var phase = new WorkflowPhase
        {
            Id = "1",
            Title = "Define",
            TemplatePath = "templates/core/01_Project_Definition.md",
            Steps = [new WorkflowStep { Number = 1, Title = "Purpose", PhaseId = "1" }],
        };
        var progress = new[]
        {
            new StepProgress
            {
                Id = Guid.NewGuid(),
                ProjectId = Guid.NewGuid(),
                PhaseId = "1",
                StepNumber = 1,
                Status = StepStatus.Complete,
            },
        };
        var document = new WorkingDocument
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            DocumentType = DocumentType.ProjectDefinition,
            Title = "Project Definition",
            RelativeMarkdownPath = "01_Project_Definition/01_Project_Definition.md",
            Fields =
            [
                new DocumentField { Key = "WHY", Label = "Why", IsRequired = true, Value = "Because" },
            ],
        };

        Assert.True(PhaseGateRules.CanPass(phase, progress, document));
    }
}
