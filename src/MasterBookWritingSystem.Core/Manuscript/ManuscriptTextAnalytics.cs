namespace MasterBookWritingSystem.Core.Manuscript;

public sealed class BracketNote
{
    public required string Marker { get; init; }

    public required int LineNumber { get; init; }

    public required string LineText { get; init; }
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
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            foreach (var marker in BracketMarkers)
            {
                if (line.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add(new BracketNote
                    {
                        Marker = marker,
                        LineNumber = index + 1,
                        LineText = line.Trim(),
                    });
                }
            }
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
}
