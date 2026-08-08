using MasterBookWritingSystem.Core.Manuscript;

namespace MasterBookWritingSystem.Tests.Manuscript;

public sealed class ChapterStructureRulesTests
{
    [Fact]
    public void EnsureValidSplitIndex_RejectsCutThroughSceneMarkers()
    {
        var sceneId = Guid.NewGuid();
        var markdown =
            "Lead\n"
            + SceneProseMarkers.FormatOpen(sceneId) + "\n"
            + "body\n"
            + SceneProseMarkers.CloseMarker + "\n"
            + "Tail\n";
        var inside = markdown.IndexOf("body", StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            ChapterStructureRules.EnsureValidSplitIndex(markdown, inside));
    }

    [Fact]
    public void SplitAndMerge_RoundTripPreservesProseOrder()
    {
        const string markdown = "AAAA\n\nBBBB\n";
        var (left, right) = ChapterStructureRules.SplitMarkdown(markdown, 6);
        Assert.Equal("AAAA\n\n", left);
        Assert.Equal("BBBB\n", right);
        var merged = ChapterStructureRules.MergeMarkdown(left, right);
        Assert.Contains("AAAA", merged, StringComparison.Ordinal);
        Assert.Contains("BBBB", merged, StringComparison.Ordinal);
        Assert.True(merged.IndexOf("AAAA", StringComparison.Ordinal)
            < merged.IndexOf("BBBB", StringComparison.Ordinal));
    }
}
