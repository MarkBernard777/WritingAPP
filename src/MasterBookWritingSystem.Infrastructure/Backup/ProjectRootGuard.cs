using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;

namespace MasterBookWritingSystem.Infrastructure.Backup;

internal static class ProjectRootGuard
{
    public static string RequireActiveRoot(IProjectService projects, Guid projectId)
    {
        var active = projects.ActiveProject
            ?? throw new InvalidOperationException("Open a project before using backup or export services.");
        if (active.Id != projectId)
        {
            throw new InvalidOperationException("The requested project is not the active project.");
        }

        return active.RootPath;
    }

    public static string EnsureUniqueExportPath(string projectRoot, string fileName)
    {
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.Contains("..", StringComparison.Ordinal)
            || fileName.Contains('/')
            || fileName.Contains('\\'))
        {
            throw new InvalidOperationException("Export names must be simple file or folder names without path segments.");
        }

        var absolute = Path.Combine(projectRoot, "Exports", fileName);
        if (File.Exists(absolute) || Directory.Exists(absolute))
        {
            throw new InvalidOperationException($"Export already exists: Exports/{fileName}. Choose a different name.");
        }

        Directory.CreateDirectory(Path.Combine(projectRoot, "Exports"));
        return absolute;
    }

    public static string ToRelativeExport(string projectRoot, string absolutePath)
    {
        var relative = Path.GetRelativePath(projectRoot, absolutePath).Replace('\\', '/');
        return relative;
    }
}
