using System.Text.RegularExpressions;

namespace MasterBookWritingSystem.Core.Manuscript;

public enum ManuscriptSearchScopeKind
{
    EntireManuscript = 0,
    Book = 1,
    Part = 2,
    SelectedChapters = 3,
}

public sealed class ManuscriptSearchOptions
{
    public required string FindText { get; init; }

    public string ReplacementText { get; init; } = string.Empty;

    public bool CaseSensitive { get; init; }

    public bool WholeWord { get; init; }

    public bool UseRegex { get; init; }

    public ManuscriptSearchScopeKind ScopeKind { get; init; } = ManuscriptSearchScopeKind.EntireManuscript;

    public Guid? BookId { get; init; }

    public Guid? PartId { get; init; }

    public IReadOnlyList<Guid> SelectedChapterIds { get; init; } = [];

    /// <summary>Context characters on each side of a match for preview.</summary>
    public int ContextRadius { get; init; } = 40;

    public TimeSpan RegexMatchTimeout { get; init; } = TimeSpan.FromMilliseconds(250);
}

public sealed class ManuscriptSearchHit
{
    public required Guid ChapterId { get; init; }

    public required string ChapterTitle { get; init; }

    public required int ChapterSequence { get; init; }

    public required int StartIndex { get; init; }

    public required int Length { get; init; }

    public required string MatchedText { get; init; }

    public required string ContextBefore { get; init; }

    public required string ContextAfter { get; init; }

    public required string ProposedReplacement { get; init; }

    public bool Include { get; set; } = true;
}

public static class ManuscriptSearchMatching
{
    public static void ValidateOptions(ManuscriptSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.FindText))
        {
            throw new InvalidOperationException("Search text is required.");
        }

        if (options.ScopeKind == ManuscriptSearchScopeKind.Book && options.BookId is null)
        {
            throw new InvalidOperationException("Book scope requires a book id.");
        }

        if (options.ScopeKind == ManuscriptSearchScopeKind.Part && options.PartId is null)
        {
            throw new InvalidOperationException("Part scope requires a part id.");
        }

        if (options.ScopeKind == ManuscriptSearchScopeKind.SelectedChapters
            && options.SelectedChapterIds.Count == 0)
        {
            throw new InvalidOperationException("Selected-chapters scope requires at least one chapter.");
        }

        if (options.UseRegex)
        {
            try
            {
                _ = new Regex(
                    options.FindText,
                    BuildRegexOptions(options),
                    options.RegexMatchTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"Invalid regular expression: {ex.Message}", ex);
            }
        }
    }

    public static IReadOnlyList<ManuscriptSearchHit> FindInChapter(
        Guid chapterId,
        string chapterTitle,
        int chapterSequence,
        string markdown,
        ManuscriptSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ValidateOptions(options);

        var hits = new List<ManuscriptSearchHit>();
        if (options.UseRegex)
        {
            var regex = new Regex(
                options.FindText,
                BuildRegexOptions(options),
                options.RegexMatchTimeout);
            foreach (Match match in regex.Matches(markdown))
            {
                if (!match.Success || match.Length == 0)
                {
                    continue;
                }

                hits.Add(ToHit(
                    chapterId,
                    chapterTitle,
                    chapterSequence,
                    markdown,
                    match.Index,
                    match.Length,
                    match.Value,
                    options));
            }

            return hits;
        }

        var comparison = options.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var needle = options.FindText;
        var start = 0;
        while (start <= markdown.Length - needle.Length)
        {
            var index = markdown.IndexOf(needle, start, comparison);
            if (index < 0)
            {
                break;
            }

            if (!options.WholeWord || IsWholeWord(markdown, index, needle.Length))
            {
                var matched = markdown.Substring(index, needle.Length);
                hits.Add(ToHit(
                    chapterId,
                    chapterTitle,
                    chapterSequence,
                    markdown,
                    index,
                    needle.Length,
                    matched,
                    options));
            }

            start = index + Math.Max(1, needle.Length);
        }

        return hits;
    }

    public static string ApplyHitsToChapter(
        string markdown,
        IEnumerable<ManuscriptSearchHit> includedHitsForChapter)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var ordered = includedHitsForChapter
            .Where(hit => hit.Include)
            .OrderByDescending(hit => hit.StartIndex)
            .ToList();
        var buffer = markdown;
        foreach (var hit in ordered)
        {
            if (hit.StartIndex < 0 || hit.StartIndex + hit.Length > buffer.Length)
            {
                throw new InvalidOperationException(
                    $"Stale search hit in chapter '{hit.ChapterTitle}' — re-run preview.");
            }

            var actual = buffer.Substring(hit.StartIndex, hit.Length);
            if (!string.Equals(actual, hit.MatchedText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Chapter '{hit.ChapterTitle}' changed since preview — re-run search.");
            }

            buffer = buffer.Substring(0, hit.StartIndex)
                + hit.ProposedReplacement
                + buffer.Substring(hit.StartIndex + hit.Length);
        }

        return buffer;
    }

    private static ManuscriptSearchHit ToHit(
        Guid chapterId,
        string chapterTitle,
        int chapterSequence,
        string markdown,
        int index,
        int length,
        string matched,
        ManuscriptSearchOptions options)
    {
        var beforeStart = Math.Max(0, index - options.ContextRadius);
        var afterEnd = Math.Min(markdown.Length, index + length + options.ContextRadius);
        return new ManuscriptSearchHit
        {
            ChapterId = chapterId,
            ChapterTitle = chapterTitle,
            ChapterSequence = chapterSequence,
            StartIndex = index,
            Length = length,
            MatchedText = matched,
            ContextBefore = markdown.Substring(beforeStart, index - beforeStart),
            ContextAfter = markdown.Substring(index + length, afterEnd - (index + length)),
            ProposedReplacement = options.UseRegex
                ? new Regex(
                        options.FindText,
                        BuildRegexOptions(options),
                        options.RegexMatchTimeout)
                    .Replace(matched, options.ReplacementText ?? string.Empty, 1)
                : options.ReplacementText ?? string.Empty,
            Include = true,
        };
    }

    private static RegexOptions BuildRegexOptions(ManuscriptSearchOptions options)
    {
        var regexOptions = RegexOptions.CultureInvariant;
        if (!options.CaseSensitive)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        return regexOptions;
    }

    private static bool IsWholeWord(string text, int index, int length)
    {
        var beforeOk = index == 0 || !IsWordChar(text[index - 1]);
        var afterIndex = index + length;
        var afterOk = afterIndex >= text.Length || !IsWordChar(text[afterIndex]);
        return beforeOk && afterOk;
    }

    private static bool IsWordChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
}
