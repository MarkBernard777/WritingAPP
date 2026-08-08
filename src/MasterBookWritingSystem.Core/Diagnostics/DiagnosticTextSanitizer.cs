namespace MasterBookWritingSystem.Core.Diagnostics;

/// <summary>
/// Keeps diagnostic/log text free of manuscript prose and other long user payloads.
/// </summary>
public static class DiagnosticTextSanitizer
{
    public const int DefaultMaxLength = 240;

    public static string Sanitize(string? message, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var text = message.Replace("\r", " ").Replace("\n", " ").Trim();

        // Explicit redaction when exception text accidentally embeds draft payloads.
        if (LooksLikeManuscriptPayload(text))
        {
            return "[redacted: user manuscript or draft content omitted from diagnostics]";
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..Math.Max(0, maxLength - 3)] + "...";
    }

    public static bool LooksLikeManuscriptPayload(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Long multi-sentence prose is treated as manuscript/draft content.
        if (text.Length >= 400 && text.Count(character => character == ' ') >= 40)
        {
            return true;
        }

        if (text.Contains("SECRET_MANUSCRIPT_", StringComparison.Ordinal)
            || text.Contains("DRAFT_PAYLOAD_", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public static bool ContainsManuscriptPayload(string? haystack, string manuscriptMarker)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(manuscriptMarker))
        {
            return false;
        }

        return haystack.Contains(manuscriptMarker, StringComparison.Ordinal);
    }
}
