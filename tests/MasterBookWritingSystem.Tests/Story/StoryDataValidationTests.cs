using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Story;

namespace MasterBookWritingSystem.Tests.Story;

public class StoryDataValidationTests
{
    [Fact]
    public void Character_RequiresName()
    {
        var result = StoryDataValidation.Validate(new Character
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = " ",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Beat_RequiresPositiveNumberAndName()
    {
        var result = StoryDataValidation.Validate(new Beat
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Number = 0,
            Name = "",
        });

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Scene_RejectsSelfNextLinkAndNegativeWordCount()
    {
        var id = Guid.NewGuid();
        var result = StoryDataValidation.Validate(new Scene
        {
            Id = id,
            ProjectId = Guid.NewGuid(),
            SequenceNumber = 1,
            Title = "Clash",
            NextSceneId = id,
            WordCount = -1,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("itself", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("word count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WorldEntry_AcceptsDepthClassification()
    {
        var result = StoryDataValidation.Validate(new WorldEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "Ash Harbor",
            Category = "Location",
            Depth = WorldDepth.AuthorOnly,
            TravelTime = "2 days",
        });

        Assert.True(result.IsValid);
    }
}
