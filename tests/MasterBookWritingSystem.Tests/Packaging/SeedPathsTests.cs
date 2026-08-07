using MasterBookWritingSystem.Core.Abstractions;

namespace MasterBookWritingSystem.Tests.Packaging;

public sealed class SeedPathsTests
{
    [Fact]
    public void ValidateRequiredSeedFiles_AcceptsCompleteSeedTree()
    {
        var temp = Path.Combine(Path.GetTempPath(), "mbws-seed-ok", Guid.NewGuid().ToString("N"));
        var seed = Path.Combine(temp, "seed");
        Directory.CreateDirectory(Path.Combine(seed, "schemas"));
        Directory.CreateDirectory(Path.Combine(seed, "templates", "core"));
        File.WriteAllText(SeedPaths.GetWorkflowPath(seed), "{}");
        File.WriteAllText(SeedPaths.GetDocumentTemplatesPath(seed), "{}");
        File.WriteAllText(Path.Combine(SeedPaths.GetCoreTemplatesDirectory(seed), "01.md"), "# Title");

        try
        {
            Assert.Empty(SeedPaths.ValidateRequiredSeedFiles(seed));
            Assert.EndsWith("workflow.json", SeedPaths.GetWorkflowPath(seed), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ValidateRequiredSeedFiles_ReportsMissingPieces()
    {
        var temp = Path.Combine(Path.GetTempPath(), "mbws-seed-missing", Guid.NewGuid().ToString("N"), "seed");
        Directory.CreateDirectory(temp);
        try
        {
            var errors = SeedPaths.ValidateRequiredSeedFiles(temp);
            Assert.NotEmpty(errors);
            Assert.Contains(errors, error => error.Contains("workflow.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(temp)!, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void FindSeedRoot_FindsRepositoryOrBundledSeed()
    {
        var root = SeedPaths.FindSeedRoot();
        Assert.True(Directory.Exists(root));
        Assert.True(File.Exists(SeedPaths.GetWorkflowPath(root)));
        Assert.True(File.Exists(SeedPaths.GetDocumentTemplatesPath(root)));
        Assert.True(Directory.Exists(SeedPaths.GetCoreTemplatesDirectory(root)));
        Assert.Empty(SeedPaths.ValidateRequiredSeedFiles(root));
    }

    [Fact]
    public void TryFindSeedRoot_UsesBaseDirectoryWhenSeedIsPresent()
    {
        var baseSeed = Path.Combine(AppContext.BaseDirectory, "seed");
        // Infrastructure/App builds may not copy seed into the test output; skip if absent.
        if (!Directory.Exists(baseSeed))
        {
            return;
        }

        Assert.True(SeedPaths.TryFindSeedRoot(out var found));
        Assert.Equal(Path.GetFullPath(baseSeed), Path.GetFullPath(found));
    }
}
