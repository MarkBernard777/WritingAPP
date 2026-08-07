using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Story;

namespace MasterBookWritingSystem.Tests.Story;

public sealed class SceneChapterOrderingTests
{
    [Fact]
    public void OrdersAssignedBeforeUnassigned_ByChapterThenSceneSequence()
    {
        var chapterA = Guid.NewGuid();
        var chapterB = Guid.NewGuid();
        var scenes = new[]
        {
            new Scene { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), SequenceNumber = 2, Title = "A2", ChapterId = chapterA },
            new Scene { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), SequenceNumber = 1, Title = "B1", ChapterId = chapterB },
            new Scene { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), SequenceNumber = 1, Title = "A1", ChapterId = chapterA },
            new Scene { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), SequenceNumber = 3, Title = "U", ChapterId = null },
        };

        var ordered = SceneChapterOrdering.OrderByChapterThenSequence(
            scenes,
            new Dictionary<Guid, int> { [chapterA] = 1, [chapterB] = 2 });

        Assert.Equal(["A1", "A2", "B1", "U"], ordered.Select(item => item.Title));
    }
}
