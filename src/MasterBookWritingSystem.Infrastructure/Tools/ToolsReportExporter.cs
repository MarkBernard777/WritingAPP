using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Infrastructure.IO;

namespace MasterBookWritingSystem.Infrastructure.Tools;

public sealed class ToolsReportExporter : IToolsReportExporter
{
    private readonly IProjectService _projectService;

    public ToolsReportExporter(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public async Task<string> ExportMarkdownAsync(
        Guid projectId,
        string exportFileName,
        string markdownContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportFileName);
        ArgumentNullException.ThrowIfNull(markdownContent);

        if (exportFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || exportFileName.Contains("..", StringComparison.Ordinal)
            || exportFileName.Contains('/')
            || exportFileName.Contains('\\'))
        {
            throw new InvalidOperationException("Export file name must be a simple file name without path segments.");
        }

        if (!exportFileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            exportFileName += ".md";
        }

        var root = RequireActiveRoot(projectId);
        var absolute = Path.Combine(root, "Exports", exportFileName);
        if (File.Exists(absolute))
        {
            throw new InvalidOperationException(
                $"Export file already exists: Exports/{exportFileName}. Choose a different file name.");
        }

        await AtomicFileWriter.WriteAllTextAsync(absolute, markdownContent, cancellationToken)
            .ConfigureAwait(false);
        return $"Exports/{exportFileName}".Replace('\\', '/');
    }

    private string RequireActiveRoot(Guid projectId)
    {
        var active = _projectService.ActiveProject
            ?? throw new InvalidOperationException("Open a project before exporting tool reports.");
        if (active.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return active.RootPath;
    }
}
