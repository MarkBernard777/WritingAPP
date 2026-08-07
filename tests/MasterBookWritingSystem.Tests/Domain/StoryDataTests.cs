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
            RelativeMarkdownPath = "01 Project Definition/01_Project_Definition.md",
            CompletionPercentage = 25,
        };

        Assert.Equal(DocumentType.ProjectDefinition, document.DocumentType);
        Assert.Equal(25, document.CompletionPercentage);
    }

    [Fact]
    public void Character_UsesStableGuidAndExpandedFields()
    {
        var character = new Character
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "Kael",
            Role = "Protagonist",
            Goal = "Restore the broken oath",
            Fear = "Becoming the tyrant he opposes",
            Contradiction = "Needs allies but trusts no one",
            BookArc = "Exile to steward",
            IsViewpoint = true,
        };

        Assert.NotEqual(Guid.Empty, character.Id);
        Assert.Equal("Kael", character.Name);
        Assert.True(character.IsViewpoint);
        Assert.Equal("Exile to steward", character.BookArc);
    }

    [Fact]
    public void Scene_LinksToChapterSequenceAndNextScene()
    {
        var nextId = Guid.NewGuid();
        var scene = new Scene
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ChapterId = Guid.NewGuid(),
            SequenceNumber = 3,
            Title = "The Market Betrayal",
            ViewpointCharacterId = Guid.NewGuid(),
            Location = "Market square",
            Goal = "Buy passage",
            NextSceneId = nextId,
            Status = SceneDraftStatus.Outlined,
            WordCount = 850,
        };

        Assert.Equal(3, scene.SequenceNumber);
        Assert.Equal(SceneDraftStatus.Outlined, scene.Status);
        Assert.Equal(nextId, scene.NextSceneId);
        Assert.Equal("Market square", scene.Location);
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
    public void Beat_RecordsStructuralTurningPointAndEscalation()
    {
        var beat = new Beat
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Number = 1,
            Name = "Opening Image",
            Summary = "Ash falls on the capital.",
            Cause = "War ends",
            Consequence = "Refugees flood the gates",
            Escalation = "Low",
            Status = BeatStatus.Planned,
        };

        Assert.Equal(1, beat.Number);
        Assert.Equal("Opening Image", beat.Name);
        Assert.Equal("Low", beat.Escalation);
    }

    [Fact]
    public void WorldEntry_TracksDepthAndTravel()
    {
        var entry = new WorldEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "Ash Harbor",
            Category = "Location",
            Depth = WorldDepth.OnPage,
            TravelDistance = "40 leagues",
            TravelTime = "3 days",
            CanonicalFacts = "Free port",
        };

        Assert.Equal(WorldDepth.OnPage, entry.Depth);
        Assert.Equal("3 days", entry.TravelTime);
    }
}
