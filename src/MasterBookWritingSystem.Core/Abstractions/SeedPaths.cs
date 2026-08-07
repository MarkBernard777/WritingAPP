namespace MasterBookWritingSystem.Core.Abstractions;

/// <summary>
/// Locates the read-only seed content directory shipped with the application.
/// Installed/published builds place seed under AppContext.BaseDirectory; parent walking is a development fallback only.
/// </summary>
public static class SeedPaths
{
    public const string SeedDirectoryName = "seed";

    public const string WorkflowRelativePath = "workflow.json";

    public const string DocumentTemplatesRelativePath = "schemas/document-templates.json";

    public const string CoreTemplatesRelativeDirectory = "templates/core";

    public static bool TryFindSeedRoot(out string seedRoot)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, SeedDirectoryName);
        if (Directory.Exists(bundled))
        {
            seedRoot = bundled;
            return true;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory).Parent;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, SeedDirectoryName);
            if (Directory.Exists(candidate))
            {
                seedRoot = candidate;
                return true;
            }

            dir = dir.Parent;
        }

        seedRoot = string.Empty;
        return false;
    }

    public static string FindSeedRoot()
    {
        if (TryFindSeedRoot(out var seedRoot))
        {
            return seedRoot;
        }

        throw new FileNotFoundException(
            $"Could not locate the '{SeedDirectoryName}' directory beside the application or in a parent folder.");
    }

    public static string GetWorkflowPath(string seedRoot)
        => Path.Combine(seedRoot, WorkflowRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string GetDocumentTemplatesPath(string seedRoot)
        => Path.Combine(seedRoot, DocumentTemplatesRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string GetCoreTemplatesDirectory(string seedRoot)
        => Path.Combine(seedRoot, CoreTemplatesRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Validates that required seed files exist. Returns an empty list when healthy.
    /// </summary>
    public static IReadOnlyList<string> ValidateRequiredSeedFiles(string? seedRoot = null)
    {
        var errors = new List<string>();
        string root;
        if (!string.IsNullOrWhiteSpace(seedRoot))
        {
            root = seedRoot;
        }
        else if (!TryFindSeedRoot(out root!))
        {
            errors.Add(
                $"Required '{SeedDirectoryName}' folder was not found under '{AppContext.BaseDirectory}'. "
                + "Reinstall the application or restore the seed content next to the executable.");
            return errors;
        }

        if (!Directory.Exists(root))
        {
            errors.Add($"Seed directory is missing: {root}");
            return errors;
        }

        var workflow = GetWorkflowPath(root);
        if (!File.Exists(workflow))
        {
            errors.Add($"Required seed file is missing: {WorkflowRelativePath}");
        }

        var templates = GetDocumentTemplatesPath(root);
        if (!File.Exists(templates))
        {
            errors.Add($"Required seed file is missing: {DocumentTemplatesRelativePath}");
        }

        var coreTemplates = GetCoreTemplatesDirectory(root);
        if (!Directory.Exists(coreTemplates))
        {
            errors.Add($"Required seed folder is missing: {CoreTemplatesRelativeDirectory}");
        }
        else if (!Directory.EnumerateFiles(coreTemplates, "*.md", SearchOption.TopDirectoryOnly).Any())
        {
            errors.Add($"Required seed folder has no markdown templates: {CoreTemplatesRelativeDirectory}");
        }

        return errors;
    }
}
