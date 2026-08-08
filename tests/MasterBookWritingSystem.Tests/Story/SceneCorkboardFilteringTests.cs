using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Story;

namespace MasterBookWritingSystem.Tests.Story;

public sealed class SceneCorkboardFilteringTests
{
    private readonly Guid _bookA = Guid.NewGuid();
    private readonly Guid _bookB = Guid.NewGuid();
    private readonly Guid _partA1 = Guid.NewGuid();
    private readonly Guid _partB1 = Guid.NewGuid();
    private readonly Guid _chapterA = Guid.NewGuid();
    private readonly Guid _chapterB = Guid.NewGuid();
    private readonly Guid _pov = Guid.NewGuid();

    [Fact]
    public void Filters_Compose_BookPartChapterViewpointStatus()
    {
        var scenes = new[]
        {
            Scene("A1", _chapterA, 1, _pov, SceneDraftStatus.Outlined),
            Scene("A2", _chapterA, 2, null, SceneDraftStatus.Drafting),
            Scene("B1", _chapterB, 1, _pov, SceneDraftStatus.Outlined),
            Scene("U", null, 1, _pov, SceneDraftStatus.Complete),
        };

        var chapterPart = new Dictionary<Guid, Guid?>
        {
            [_chapterA] = _partA1,
            [_chapterB] = _partB1,
        };
        var partBook = new Dictionary<Guid, Guid>
        {
            [_partA1] = _bookA,
            [_partB1] = _bookB,
        };
        var chapterSeq = new Dictionary<Guid, int>
        {
            [_chapterA] = 1,
            [_chapterB] = 2,
        };

        var byBook = SceneCorkboardFiltering.Apply(
            scenes,
            new SceneCorkboardFilter { BookId = _bookA },
            chapterPart,
            partBook,
            chapterSeq);
        Assert.Equal(["A1", "A2"], byBook.Select(s => s.Title));

        var byPartAndStatus = SceneCorkboardFiltering.Apply(
            scenes,
            new SceneCorkboardFilter { PartId = _partA1, Status = SceneDraftStatus.Drafting },
            chapterPart,
            partBook,
            chapterSeq);
        Assert.Equal(["A2"], byPartAndStatus.Select(s => s.Title));

        var byChapterAndPov = SceneCorkboardFiltering.Apply(
            scenes,
            new SceneCorkboardFilter { ChapterId = _chapterA, ViewpointCharacterId = _pov },
            chapterPart,
            partBook,
            chapterSeq);
        Assert.Equal(["A1"], byChapterAndPov.Select(s => s.Title));

        var unassigned = SceneCorkboardFiltering.Apply(
            scenes,
            new SceneCorkboardFilter { UnassignedChaptersOnly = true },
            chapterPart,
            partBook,
            chapterSeq);
        Assert.Equal(["U"], unassigned.Select(s => s.Title));
    }

    [Fact]
    public void Ordering_IsStable_ChapterThenSequence()
    {
        var scenes = new[]
        {
            Scene("B2", _chapterB, 2, null, SceneDraftStatus.Outlined),
            Scene("A1", _chapterA, 1, null, SceneDraftStatus.Outlined),
            Scene("B1", _chapterB, 1, null, SceneDraftStatus.Outlined),
            Scene("A2", _chapterA, 2, null, SceneDraftStatus.Outlined),
        };
        var ordered = SceneCorkboardFiltering.Apply(
            scenes,
            new SceneCorkboardFilter(),
            new Dictionary<Guid, Guid?> { [_chapterA] = _partA1, [_chapterB] = _partB1 },
            new Dictionary<Guid, Guid> { [_partA1] = _bookA, [_partB1] = _bookB },
            new Dictionary<Guid, int> { [_chapterA] = 1, [_chapterB] = 2 });

        Assert.Equal(["A1", "A2", "B1", "B2"], ordered.Select(s => s.Title));
    }

    private static Scene Scene(
        string title,
        Guid? chapterId,
        int sequence,
        Guid? viewpointId,
        SceneDraftStatus status)
        => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ChapterId = chapterId,
            SequenceNumber = sequence,
            Title = title,
            ViewpointCharacterId = viewpointId,
            Status = status,
        };
}
