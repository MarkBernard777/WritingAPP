using MasterBookWritingSystem.Core.Manuscript;

namespace MasterBookWritingSystem.Core.Tools;

public sealed class TextAnalyzerResult
{
    public required int WordCount { get; init; }

    public required double ReadingTimeMinutes { get; init; }

    public required int LongSentenceCount { get; init; }

    public required IReadOnlyList<string> LongSentenceSamples { get; init; }

    public required IReadOnlyList<(string Term, int Count)> RepeatedTerms { get; init; }

    public required IReadOnlyList<(string Word, int Count)> FilterWords { get; init; }

    public required IReadOnlyList<BracketNote> DraftingMarkers { get; init; }

    public string Explanation { get; init; } =
        "Reading time assumes 250 words/minute. Long sentences exceed 25 words. Repeated terms appear 5+ times (excluding stop words). Filter words and [CHECK]/[FIX]/[REWRITE] markers are listed for revision.";
}

public static class TextAnalyzerCalculator
{
    public const int WordsPerMinute = 250;

    public const int LongSentenceWordThreshold = 25;

    public const int RepeatedTermMinimum = 5;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "if", "in", "on", "at", "to", "for", "of", "as", "is", "was",
        "were", "be", "been", "being", "it", "its", "this", "that", "with", "from", "by", "he", "she", "they",
        "them", "his", "her", "their", "i", "you", "we", "my", "your", "our", "not", "so", "than", "then",
        "there", "here", "when", "where", "what", "who", "how", "which", "into", "out", "up", "down", "over",
    };

    private static readonly HashSet<string> FilterWordSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "just", "really", "very", "quite", "rather", "somewhat", "almost", "perhaps", "maybe", "seemed",
        "appeared", "suddenly", "began", "started", "felt", "thought", "knew", "looked", "watched", "noticed",
    };

    public static TextAnalyzerResult Analyze(string? markdown)
    {
        var text = markdown ?? string.Empty;
        var wordCount = ManuscriptTextAnalytics.CountWords(text);
        var reading = wordCount == 0 ? 0 : Math.Round(wordCount / (double)WordsPerMinute, 2);
        var sentences = SplitSentences(text);
        var longSentences = sentences
            .Select(sentence => (Sentence: sentence, Words: ManuscriptTextAnalytics.CountWords(sentence)))
            .Where(item => item.Words > LongSentenceWordThreshold)
            .ToList();

        var tokens = Tokenize(text);
        var repeated = tokens
            .Where(token => !StopWords.Contains(token) && token.Length > 2)
            .GroupBy(token => token, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Term: group.Key, Count: group.Count()))
            .Where(item => item.Count >= RepeatedTermMinimum)
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Term, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        var filters = tokens
            .Where(token => FilterWordSet.Contains(token))
            .GroupBy(token => token.ToLowerInvariant())
            .Select(group => (Word: group.Key, Count: group.Count()))
            .OrderByDescending(item => item.Count)
            .ToList();

        return new TextAnalyzerResult
        {
            WordCount = wordCount,
            ReadingTimeMinutes = reading,
            LongSentenceCount = longSentences.Count,
            LongSentenceSamples = longSentences.Select(item => item.Sentence.Trim()).Take(5).ToList(),
            RepeatedTerms = repeated,
            FilterWords = filters,
            DraftingMarkers = ManuscriptTextAnalytics.FindBracketNotes(text),
        };
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(sentence => sentence.Length > 0);
    }

    private static List<string> Tokenize(string text)
    {
        var list = new List<string>();
        var current = new List<char>();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '\'')
            {
                current.Add(char.ToLowerInvariant(ch));
            }
            else if (current.Count > 0)
            {
                list.Add(new string(current.ToArray()));
                current.Clear();
            }
        }

        if (current.Count > 0)
        {
            list.Add(new string(current.ToArray()));
        }

        return list;
    }
}
