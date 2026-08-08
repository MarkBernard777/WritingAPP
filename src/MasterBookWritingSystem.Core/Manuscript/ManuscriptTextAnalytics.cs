namespace MasterBookWritingSystem.Core.Manuscript;

public sealed class BracketNote
{
    public required string Marker { get; init; }

    public required int LineNumber { get; init; }

    public required string LineText { get; init; }

    /// <summary>UTF-16 offset of the marker within the original markdown buffer.</summary>
    public required int CharIndex { get; init; }

    public required int MarkerLength { get; init; }
}

public sealed class ManuscriptCompileResult
{
    public required string AbsolutePath { get; init; }

    public required string RelativePath { get; init; }

    public required int ChapterCount { get; init; }

    public required int WordCount { get; init; }
}

public static class ManuscriptTextAnalytics
{
    public static readonly string[] BracketMarkers = ["[CHECK]", "[FIX]", "[REWRITE]"];

    public static int CountWords(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return 0;
        }

        return markdown
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    public static IReadOnlyList<BracketNote> FindBracketNotes(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return [];
        }

        var notes = new List<BracketNote>();
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var offset = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            foreach (var marker in BracketMarkers)
            {
                var at = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                {
                    // Map normalized offset back into the original buffer when CRLF is used.
                    var charIndex = MapNormalizedOffsetToOriginal(markdown, offset + at);
                    notes.Add(new BracketNote
                    {
                        Marker = marker,
                        LineNumber = index + 1,
                        LineText = line.Trim(),
                        CharIndex = charIndex,
                        MarkerLength = marker.Length,
                    });
                }
            }

            offset += line.Length + 1;
        }

        return notes;
    }

    public static string SanitizeFileStem(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "chapter";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = title.Trim().Select(ch => invalid.Contains(ch) || ch is '/' or '\\' ? '-' : ch).ToArray();
        var cleaned = new string(chars).Trim(' ', '.', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? "chapter" : cleaned;
    }

    private static int MapNormalizedOffsetToOriginal(string original, int normalizedOffset)
    {
        if (!original.Contains("\r\n", StringComparison.Ordinal))
        {
            return Math.Clamp(normalizedOffset, 0, original.Length);
        }

        var originalIndex = 0;
        var normalizedIndex = 0;
        while (originalIndex < original.Length && normalizedIndex < normalizedOffset)
        {
            if (originalIndex + 1 < original.Length
                && original[originalIndex] == '\r'
                && original[originalIndex + 1] == '\n')
            {
                originalIndex += 2;
                normalizedIndex++;
                continue;
            }

            originalIndex++;
            normalizedIndex++;
        }

        return Math.Clamp(originalIndex, 0, original.Length);
    }
}
