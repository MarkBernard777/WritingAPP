using MasterBookWritingSystem.Core.Manuscript;

namespace MasterBookWritingSystem.Tests.Manuscript;

public sealed class ManuscriptSearchMatchingTests
{
    private readonly Guid _chapterId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void FindInChapter_RespectsCaseAndWholeWord()
    {
        const string markdown = "The cat scattered. Cat sat. category.";
        var insensitive = ManuscriptSearchMatching.FindInChapter(
            _chapterId,
            "Ch",
            1,
            markdown,
            new ManuscriptSearchOptions { FindText = "cat", CaseSensitive = false, WholeWord = false });
        Assert.Equal(4, insensitive.Count);

        var whole = ManuscriptSearchMatching.FindInChapter(
            _chapterId,
            "Ch",
            1,
            markdown,
            new ManuscriptSearchOptions { FindText = "cat", CaseSensitive = false, WholeWord = true });
        Assert.Equal(2, whole.Count);

        var sensitive = ManuscriptSearchMatching.FindInChapter(
            _chapterId,
            "Ch",
            1,
            markdown,
            new ManuscriptSearchOptions { FindText = "Cat", CaseSensitive = true, WholeWord = true });
        Assert.Single(sensitive);
        Assert.Equal("Cat", sensitive[0].MatchedText);
    }

    [Fact]
    public void FindInChapter_Regex_IsValidatedAndTimed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ManuscriptSearchMatching.ValidateOptions(new ManuscriptSearchOptions
            {
                FindText = "(",
                UseRegex = true,
            }));

        var hits = ManuscriptSearchMatching.FindInChapter(
            _chapterId,
            "Ch",
            1,
            "alpha beta alpha",
            new ManuscriptSearchOptions
            {
                FindText = @"\balpha\b",
                UseRegex = true,
                ReplacementText = "ALPHA",
            });
        Assert.Equal(2, hits.Count);
        Assert.Equal("ALPHA", hits[0].ProposedReplacement);
    }

    [Fact]
    public void ApplyHits_ReplacesOnlyIncluded_AndPreservesOrder()
    {
        const string markdown = "one two one";
        var hits = ManuscriptSearchMatching.FindInChapter(
            _chapterId,
            "Ch",
            1,
            markdown,
            new ManuscriptSearchOptions { FindText = "one", ReplacementText = "1" }).ToList();
        hits[1].Include = false;
        var result = ManuscriptSearchMatching.ApplyHitsToChapter(markdown, hits);
        Assert.Equal("1 two one", result);
    }

    [Fact]
    public void Preview_IncludesChapterContext()
    {
        var hits = ManuscriptSearchMatching.FindInChapter(
            _chapterId,
            "Chapter One",
            3,
            "xxxxFINDYyyy",
            new ManuscriptSearchOptions { FindText = "FIND", ContextRadius = 4, ReplacementText = "X" });
        var hit = Assert.Single(hits);
        Assert.Equal(_chapterId, hit.ChapterId);
        Assert.Equal("Chapter One", hit.ChapterTitle);
        Assert.Equal(3, hit.ChapterSequence);
        Assert.Equal("xxxx", hit.ContextBefore);
        Assert.Equal("Yyyy", hit.ContextAfter);
        Assert.Equal("X", hit.ProposedReplacement);
    }
}
