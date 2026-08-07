using Markdig;

namespace MasterBookWritingSystem.Infrastructure.Manuscript;

public static class MarkdownPreviewRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string ToHtmlDocument(string? markdown)
    {
        var body = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\" />"
            + "<style>"
            + "body { font-family: Georgia, 'Times New Roman', serif; font-size: 15px; line-height: 1.5; color: #1a202c; padding: 12px; }"
            + "h1, h2, h3 { font-family: Segoe UI, sans-serif; }"
            + "code, pre { font-family: Consolas, monospace; background: #f7fafc; }"
            + "blockquote { border-left: 3px solid #cbd5e0; margin-left: 0; padding-left: 12px; color: #4a5568; }"
            + "</style></head><body>"
            + body
            + "</body></html>";
    }
}
