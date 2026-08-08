namespace MasterBookWritingSystem.Core.Manuscript;

public enum MarkdownFormatKind
{
    Bold = 0,
    Italic = 1,
    Heading1 = 2,
    Heading2 = 3,
    Heading3 = 4,
    BulletList = 5,
    NumberedList = 6,
    BlockQuote = 7,
}

/// <summary>
/// Pure Markdown wrap/prefix helpers used by the drafting toolbar and shortcut tests.
/// </summary>
public static class MarkdownFormatting
{
    public static string Apply(
        string markdown,
        int selectionStart,
        int selectionLength,
        MarkdownFormatKind kind,
        out int newSelectionStart,
        out int newSelectionLength)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (selectionStart < 0 || selectionLength < 0 || selectionStart + selectionLength > markdown.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(selectionStart));
        }

        var selected = markdown.Substring(selectionStart, selectionLength);
        string replacement;
        switch (kind)
        {
            case MarkdownFormatKind.Bold:
                replacement = WrapInline(selected, "**", "**", out newSelectionStart, out newSelectionLength, selectionStart);
                break;
            case MarkdownFormatKind.Italic:
                replacement = WrapInline(selected, "*", "*", out newSelectionStart, out newSelectionLength, selectionStart);
                break;
            case MarkdownFormatKind.Heading1:
                replacement = PrefixLines(selected, "# ", out newSelectionStart, out newSelectionLength, selectionStart);
                break;
            case MarkdownFormatKind.Heading2:
                replacement = PrefixLines(selected, "## ", out newSelectionStart, out newSelectionLength, selectionStart);
                break;
            case MarkdownFormatKind.Heading3:
                replacement = PrefixLines(selected, "### ", out newSelectionStart, out newSelectionLength, selectionStart);
                break;
            case MarkdownFormatKind.BulletList:
                replacement = PrefixLines(selected, "- ", out newSelectionStart, out newSelectionLength, selectionStart);
                break;
            case MarkdownFormatKind.NumberedList:
                replacement = PrefixLines(selected, "1. ", out newSelectionStart, out newSelectionLength, selectionStart);
                break;
            case MarkdownFormatKind.BlockQuote:
                replacement = PrefixLines(selected, "> ", out newSelectionStart, out newSelectionLength, selectionStart);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return markdown.Substring(0, selectionStart)
            + replacement
            + markdown.Substring(selectionStart + selectionLength);
    }

    private static string WrapInline(
        string selected,
        string open,
        string close,
        out int newStart,
        out int newLength,
        int selectionStart)
    {
        if (string.IsNullOrEmpty(selected))
        {
            newStart = selectionStart + open.Length;
            newLength = 0;
            return open + close;
        }

        newStart = selectionStart;
        newLength = open.Length + selected.Length + close.Length;
        return open + selected + close;
    }

    private static string PrefixLines(
        string selected,
        string prefix,
        out int newStart,
        out int newLength,
        int selectionStart)
    {
        if (string.IsNullOrEmpty(selected))
        {
            newStart = selectionStart + prefix.Length;
            newLength = 0;
            return prefix;
        }

        var normalized = selected.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > 0 || lines.Length == 1)
            {
                lines[i] = prefix + lines[i];
            }
        }

        var replacement = string.Join('\n', lines);
        if (selected.Contains("\r\n", StringComparison.Ordinal))
        {
            replacement = replacement.Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        newStart = selectionStart;
        newLength = replacement.Length;
        return replacement;
    }
}
