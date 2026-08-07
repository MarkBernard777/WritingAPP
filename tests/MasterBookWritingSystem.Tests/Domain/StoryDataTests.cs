using MasterBookWritingSystem.Core.Domain.Documents;
using MasterBookWritingSystem.Core.Domain.Manuscript;
using MasterBookWritingSystem.Core.Domain.Story;

namespace MasterBookWritingSystem.Tests.Domain;

public class StoryDataTests
{
    [Fact]
    public void WorkingDocument_TracksCompletion()
    {
        var document = new WorkingDocument
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            DocumentType = DocumentType.ProjectDefinition,
            Title = "Project Definition",
            CompletionPercentage = 25,
        };

        Assert.Equal(DocumentType.ProjectDefinition, document.DocumentType);
        Assert.Equal(25, document.CompletionPercentage);
    }

    [Fact]
    public void Character_UsesStableGuid()
    {
        var character = new Character
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "Kael",
            Goal = "Restore the broken oath",
            Fear = "Becoming the tyrant he opposes",
        };

        Assert.NotEqual(Guid.Empty, character.Id);
        Assert.Equal("Kael", character.Name);
    }

    [Fact]
    public void Scene_LinksToChapterSequence()
    {
        var scene = new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ChapterId = Guid.NewGuid(),
            SequenceNumber = 3,
            Title = "The Market Betrayal",
            ViewpointCharacterId = Guid.NewGuid(),
            Status = SceneDraftStatus.Outlined,
        };

        Assert.Equal(3, scene.SequenceNumber);
        Assert.Equal(SceneDraftStatus.Outlined, scene.Status);
    }

    [Fact]
    public void Chapter_StoresMarkdownRelativePath()
    {
        var chapter = new Chapter
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            SequenceNumber = 1,
            Title = "Chapter One",
            RelativeMarkdownPath = "09 Draft/Chapters/01-chapter-one.md",
        };

        Assert.EndsWith(".md", chapter.RelativeMarkdownPath);
        Assert.Equal(1, chapter.SequenceNumber);
    }

    [Fact]
    public void Beat_RecordsStructuralTurningPoint()
    {
        var beat = new Beat
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Number = 1,
            Name = "Opening Image",
            Summary = "Ash falls on the capital.",
        };

        Assert.Equal(1, beat.Number);
        Assert.Equal("Opening Image", beat.Name);
    }
}
