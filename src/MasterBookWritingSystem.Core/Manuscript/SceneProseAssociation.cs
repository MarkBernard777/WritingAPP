using System.Text;
using System.Text.RegularExpressions;

namespace MasterBookWritingSystem.Core.Manuscript;

public static class SceneProseMarkers
{
    public const string OpenPattern = @"<!--\s*mbws:scene\s+id=""([0-9a-fA-F\-]{36})""\s*-->";
    public const string CloseMarker = "<!-- /mbws:scene -->";

    public static string FormatOpen(Guid sceneId)
        => $"<!-- mbws:scene id=\"{sceneId:D}\" -->";
}

public sealed class SceneProseSpan
{
    public required Guid SceneId { get; init; }

    public required int MarkerStartIndex { get; init; }

    public required int MarkerEndIndex { get; init; }

    public required int BodyStartIndex { get; init; }

    public required int BodyLength { get; init; }

    public required string Body { get; init; }
}

public sealed class SceneProseDocument
{
    public required string RawMarkdown { get; init; }

    public required IReadOnlyList<SceneProseSpan> Spans { get; init; }

    public bool IsStructured => Spans.Count > 0;

    public int? GetCaretIndexForScene(Guid sceneId)
        => Spans.FirstOrDefault(span => span.SceneId == sceneId)?.BodyStartIndex;
}

public sealed class ManuscriptAssociationWordCounts
{
    public required int ChapterWordCount { get; init; }

    public required IReadOnlyDictionary<Guid, int> SceneWordCounts { get; init; }

    public required IReadOnlyDictionary<Guid, int> ViewpointWordCounts { get; init; }
}

/// <summary>
/// In-chapter scene delimiter association. Chapter Markdown remains the sole prose store.
/// </summary>
public static partial class SceneProseAssociation
{
    private static readonly Regex OpenRegex = CreateOpenRegex();

    [GeneratedRegex(SceneProseMarkers.OpenPattern, RegexOptions.CultureInvariant)]
    private static partial Regex CreateOpenRegex();

    public static SceneProseDocument Parse(string? markdown)
    {
        markdown ??= string.Empty;
        var spans = new List<SceneProseSpan>();
        var matches = OpenRegex.Matches(markdown);
        foreach (Match open in matches)
        {
            if (!Guid.TryParse(open.Groups[1].Value, out var sceneId))
            {
                continue;
            }

            var bodyStart = open.Index + open.Length;
            if (bodyStart < markdown.Length
                && markdown[bodyStart] == '\r')
            {
                bodyStart++;
            }

            if (bodyStart < markdown.Length
                && markdown[bodyStart] == '\n')
            {
                bodyStart++;
            }

            var closeIndex = markdown.IndexOf(
                SceneProseMarkers.CloseMarker,
                bodyStart,
                StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                continue;
            }

            var bodyLength = closeIndex - bodyStart;
            // Trim a single trailing newline before the close marker for body storage.
            if (bodyLength > 0 && markdown[closeIndex - 1] == '\n')
            {
                bodyLength--;
                if (bodyLength > 0 && markdown[closeIndex - 2] == '\r')
                {
                    bodyLength--;
                }
            }

            var body = bodyLength <= 0
                ? string.Empty
                : markdown.Substring(bodyStart, bodyLength);
            var markerEnd = closeIndex + SceneProseMarkers.CloseMarker.Length;
            spans.Add(new SceneProseSpan
            {
                SceneId = sceneId,
                MarkerStartIndex = open.Index,
                MarkerEndIndex = markerEnd,
                BodyStartIndex = bodyStart,
                BodyLength = Math.Max(0, closeIndex - bodyStart),
                Body = body,
            });
        }

        return new SceneProseDocument
        {
            RawMarkdown = markdown,
            Spans = spans,
        };
    }

    public static string StripMarkers(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var withoutOpen = OpenRegex.Replace(markdown, string.Empty);
        return withoutOpen.Replace(SceneProseMarkers.CloseMarker, string.Empty, StringComparison.Ordinal);
    }

    public static string AssociateSelection(
        string markdown,
        Guid sceneId,
        int selectionStart,
        int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (selectionStart < 0 || selectionLength < 0 || selectionStart + selectionLength > markdown.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(selectionStart), "Selection is outside the markdown buffer.");
        }

        var existing = Parse(markdown);
        if (existing.Spans.Any(span => span.SceneId == sceneId))
        {
            throw new InvalidOperationException(
                $"Scene '{sceneId}' already has a prose region in this chapter.");
        }

        var selected = markdown.Substring(selectionStart, selectionLength);
        var wrapped = $"{SceneProseMarkers.FormatOpen(sceneId)}\n{selected}\n{SceneProseMarkers.CloseMarker}";
        return markdown.Substring(0, selectionStart)
            + wrapped
            + markdown.Substring(selectionStart + selectionLength);
    }

    public static string AppendEmptyRegion(string markdown, Guid sceneId)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var existing = Parse(markdown);
        if (existing.Spans.Any(span => span.SceneId == sceneId))
        {
            return markdown;
        }

        var builder = new StringBuilder(markdown);
        if (markdown.Length > 0 && !markdown.EndsWith('\n'))
        {
            builder.Append('\n');
        }

        if (markdown.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(SceneProseMarkers.FormatOpen(sceneId));
        builder.Append('\n');
        builder.Append('\n');
        builder.Append(SceneProseMarkers.CloseMarker);
        builder.Append('\n');
        return builder.ToString();
    }

    public static string UnwrapScene(string markdown, Guid sceneId)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var doc = Parse(markdown);
        var span = doc.Spans.FirstOrDefault(item => item.SceneId == sceneId);
        if (span is null)
        {
            return markdown;
        }

        return markdown.Substring(0, span.MarkerStartIndex)
            + span.Body
            + markdown.Substring(span.MarkerEndIndex);
    }

    public static ManuscriptAssociationWordCounts CalculateWordCounts(
        string? markdown,
        IReadOnlyDictionary<Guid, Guid?> sceneViewpointById)
    {
        ArgumentNullException.ThrowIfNull(sceneViewpointById);
        var doc = Parse(markdown);
        var sceneCounts = new Dictionary<Guid, int>();
        var viewpointCounts = new Dictionary<Guid, int>();

        foreach (var span in doc.Spans)
        {
            var words = ManuscriptTextAnalytics.CountWords(span.Body);
            sceneCounts[span.SceneId] = words;
            sceneViewpointById.TryGetValue(span.SceneId, out var viewpointId);
            var key = viewpointId ?? Guid.Empty;
            viewpointCounts[key] = viewpointCounts.GetValueOrDefault(key) + words;
        }

        return new ManuscriptAssociationWordCounts
        {
            ChapterWordCount = ManuscriptTextAnalytics.CountWords(StripMarkers(markdown)),
            SceneWordCounts = sceneCounts,
            ViewpointWordCounts = viewpointCounts,
        };
    }
}
