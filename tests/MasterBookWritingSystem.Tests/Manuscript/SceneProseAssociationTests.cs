using MasterBookWritingSystem.Core.Manuscript;

namespace MasterBookWritingSystem.Tests.Manuscript;

public sealed class SceneProseAssociationTests
{
    private readonly Guid _sceneA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _sceneB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly Guid _pov = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void LegacyChapter_WithoutMarkers_ParsesUnchangedAndPreservesRoundTrip()
    {
        var markdown = "# Chapter\n\nPlain prose with no scene markers.\n";
        var doc = SceneProseAssociation.Parse(markdown);

        Assert.False(doc.IsStructured);
        Assert.Empty(doc.Spans);
        Assert.Equal(markdown, doc.RawMarkdown);
        Assert.Equal(markdown, SceneProseAssociation.StripMarkers(markdown));

        var withRegion = SceneProseAssociation.AppendEmptyRegion(markdown, _sceneA);
        Assert.Contains(markdown.TrimEnd(), withRegion, StringComparison.Ordinal);
        Assert.Contains(SceneProseMarkers.FormatOpen(_sceneA), withRegion, StringComparison.Ordinal);
        var unwrapped = SceneProseAssociation.UnwrapScene(withRegion, _sceneA);
        Assert.DoesNotContain("mbws:scene", unwrapped, StringComparison.Ordinal);
        Assert.Contains("Plain prose with no scene markers.", unwrapped, StringComparison.Ordinal);
        Assert.False(SceneProseAssociation.Parse(unwrapped).IsStructured);
    }

    [Fact]
    public void Parse_ReadsSingleSpan_AndStripRemovesMarkersOnly()
    {
        var body = "Hero enters the hall.";
        var markdown =
            "Intro.\n\n"
            + SceneProseMarkers.FormatOpen(_sceneA) + "\n"
            + body + "\n"
            + SceneProseMarkers.CloseMarker + "\n\n"
            + "Outro.\n";

        var doc = SceneProseAssociation.Parse(markdown);
        Assert.True(doc.IsStructured);
        Assert.Single(doc.Spans);
        Assert.Equal(_sceneA, doc.Spans[0].SceneId);
        Assert.Equal(body, doc.Spans[0].Body.Trim());

        var stripped = SceneProseAssociation.StripMarkers(markdown);
        Assert.DoesNotContain("mbws:scene", stripped, StringComparison.Ordinal);
        Assert.Contains(body, stripped, StringComparison.Ordinal);
        Assert.Contains("Intro.", stripped, StringComparison.Ordinal);
        Assert.Contains("Outro.", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void AssociateSelection_WrapsExplicitRegion_WithoutTouchingOutsideText()
    {
        var markdown = "AAA BBB CCC";
        var selectionStart = markdown.IndexOf("BBB", StringComparison.Ordinal);
        var result = SceneProseAssociation.AssociateSelection(markdown, _sceneA, selectionStart, 3);

        Assert.Contains(SceneProseMarkers.FormatOpen(_sceneA), result, StringComparison.Ordinal);
        Assert.Contains(SceneProseMarkers.CloseMarker, result, StringComparison.Ordinal);
        Assert.StartsWith("AAA ", result, StringComparison.Ordinal);
        Assert.EndsWith(" CCC", result, StringComparison.Ordinal);
        var doc = SceneProseAssociation.Parse(result);
        Assert.Equal("BBB", doc.Spans.Single().Body);
    }

    [Fact]
    public void AssociateSelection_RejectsSecondSpanForSameScene()
    {
        var once = SceneProseAssociation.AppendEmptyRegion("Body\n", _sceneA);
        Assert.Throws<InvalidOperationException>(() =>
            SceneProseAssociation.AssociateSelection(once, _sceneA, 0, 4));
    }

    [Fact]
    public void UnwrapScene_KeepsProse_RemovesMarkers()
    {
        var markdown = SceneProseAssociation.AssociateSelection("Keep this prose", _sceneA, 0, "Keep this prose".Length);
        var unwrapped = SceneProseAssociation.UnwrapScene(markdown, _sceneA);
        Assert.DoesNotContain("mbws:scene", unwrapped, StringComparison.Ordinal);
        Assert.Contains("Keep this prose", unwrapped, StringComparison.Ordinal);
        Assert.False(SceneProseAssociation.Parse(unwrapped).IsStructured);
    }

    [Fact]
    public void WordCounts_BySceneChapterAndViewpoint_UseProseNotMarkers()
    {
        var markdown =
            SceneProseMarkers.FormatOpen(_sceneA) + "\n"
            + "one two three\n"
            + SceneProseMarkers.CloseMarker + "\n\n"
            + SceneProseMarkers.FormatOpen(_sceneB) + "\n"
            + "four five\n"
            + SceneProseMarkers.CloseMarker + "\n";

        var counts = SceneProseAssociation.CalculateWordCounts(
            markdown,
            new Dictionary<Guid, Guid?>
            {
                [_sceneA] = _pov,
                [_sceneB] = null,
            });

        Assert.Equal(5, counts.ChapterWordCount);
        Assert.Equal(3, counts.SceneWordCounts[_sceneA]);
        Assert.Equal(2, counts.SceneWordCounts[_sceneB]);
        Assert.Equal(3, counts.ViewpointWordCounts[_pov]);
        Assert.Equal(2, counts.ViewpointWordCounts[Guid.Empty]);
    }

    [Fact]
    public void FindSpanCaret_ReturnsBodyOffsetForNavigation()
    {
        var markdown = "Lead\n\n" + SceneProseMarkers.FormatOpen(_sceneA) + "\nBody here\n" + SceneProseMarkers.CloseMarker;
        var doc = SceneProseAssociation.Parse(markdown);
        var caret = doc.GetCaretIndexForScene(_sceneA);
        Assert.NotNull(caret);
        Assert.Equal(doc.Spans[0].BodyStartIndex, caret);
    }
}
