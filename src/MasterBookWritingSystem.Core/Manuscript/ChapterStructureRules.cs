namespace MasterBookWritingSystem.Core.Manuscript;

public static class ChapterStructureRules
{
    public static void EnsureValidSplitIndex(string markdown, int splitIndex)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (splitIndex < 0 || splitIndex > markdown.Length)
        {
            throw new InvalidOperationException("Split position is outside the chapter text.");
        }

        var doc = SceneProseAssociation.Parse(markdown);
        foreach (var span in doc.Spans)
        {
            if (splitIndex > span.MarkerStartIndex && splitIndex < span.MarkerEndIndex)
            {
                throw new InvalidOperationException(
                    "Cannot split inside a scene prose region. Move the caret outside scene markers.");
            }
        }
    }

    public static (string Left, string Right) SplitMarkdown(string markdown, int splitIndex)
    {
        EnsureValidSplitIndex(markdown, splitIndex);
        return (markdown.Substring(0, splitIndex), markdown.Substring(splitIndex));
    }

    public static string MergeMarkdown(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (string.IsNullOrWhiteSpace(left))
        {
            return right;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return left;
        }

        var leftTrim = left.TrimEnd();
        var rightTrim = right.TrimStart();
        return leftTrim + "\n\n" + rightTrim + (right.EndsWith('\n') ? "\n" : string.Empty);
    }
}
