using MasterBookWritingSystem.Core.Manuscript;

namespace MasterBookWritingSystem.Tests.Manuscript;

public class ManuscriptTextAnalyticsTests
{
    [Fact]
    public void CountWords_IgnoresExtraWhitespace()
    {
        Assert.Equal(4, ManuscriptTextAnalytics.CountWords("  one two\nthree\tfour  "));
        Assert.Equal(0, ManuscriptTextAnalytics.CountWords("   "));
    }

    [Fact]
    public void FindBracketNotes_DetectsCheckFixRewrite()
    {
        var markdown = """
            Opening line.
            Need a [CHECK] on the timeline.
            Then [FIX] this dialogue.
            Later [REWRITE] the ending.
            Plain line.
            """;

        var notes = ManuscriptTextAnalytics.FindBracketNotes(markdown);

        Assert.Equal(3, notes.Count);
        Assert.Contains(notes, note => note.Marker == "[CHECK]" && note.LineNumber == 2);
        Assert.Contains(notes, note => note.Marker == "[FIX]");
        Assert.Contains(notes, note => note.Marker == "[REWRITE]");
    }

    [Fact]
    public void SanitizeFileStem_RemovesInvalidCharacters()
    {
        var stem = ManuscriptTextAnalytics.SanitizeFileStem(@"Bad:/Name*?");
        Assert.DoesNotContain(":", stem, StringComparison.Ordinal);
        Assert.DoesNotContain("*", stem, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(stem));
    }
}
