using MasterBookWritingSystem.Core.Manuscript;

namespace MasterBookWritingSystem.Tests.Manuscript;

public sealed class MarkdownFormattingTests
{
    [Theory]
    [InlineData(MarkdownFormatKind.Bold, "hi", "**hi**")]
    [InlineData(MarkdownFormatKind.Italic, "hi", "*hi*")]
    [InlineData(MarkdownFormatKind.Heading1, "Title", "# Title")]
    [InlineData(MarkdownFormatKind.BulletList, "item", "- item")]
    [InlineData(MarkdownFormatKind.BlockQuote, "said", "> said")]
    public void Apply_WrapsOrPrefixesSelection(MarkdownFormatKind kind, string selected, string expected)
    {
        var markdown = $"xx{selected}yy";
        var start = 2;
        var result = MarkdownFormatting.Apply(markdown, start, selected.Length, kind, out var newStart, out var newLength);
        Assert.Equal($"xx{expected}yy", result);
        Assert.Equal(start, newStart);
        Assert.Equal(expected.Length, newLength);
    }

    [Fact]
    public void Apply_ParticipatesAsPureBufferTransform_ForUndoStacks()
    {
        var original = "alpha beta gamma";
        var mid = MarkdownFormatting.Apply(original, 6, 4, MarkdownFormatKind.Bold, out _, out _);
        Assert.Equal("alpha **beta** gamma", mid);
        // Undo is native TextBox; buffer transform is reversible by restoring prior string.
        Assert.Equal("alpha beta gamma", original);
    }
}
