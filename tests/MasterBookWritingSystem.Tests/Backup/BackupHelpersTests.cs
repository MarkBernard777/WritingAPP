using MasterBookWritingSystem.Core.Backup;

namespace MasterBookWritingSystem.Tests.Backup;

public class BackupHelpersTests
{
    [Theory]
    [InlineData("Exports/file.md", true)]
    [InlineData("13 Archive/Snapshots/snap/a.txt", true)]
    [InlineData("09 Draft/Chapters/01.md", false)]
    [InlineData("project.mbws", false)]
    public void ExcludedPaths_AreDetected(string path, bool excluded)
        => Assert.Equal(excluded, BackupPathRules.IsExcludedRelativePath(path));

    [Fact]
    public void Manifest_RoundTrips()
    {
        var manifest = new SnapshotManifest
        {
            ProjectId = Guid.NewGuid(),
            ProjectTitle = "Test",
            SnapshotName = "snapshot-1",
            SchemaVersion = 5,
            CreatedUtc = DateTimeOffset.UtcNow,
            Files =
            [
                new SnapshotFileEntry { RelativePath = "project.json", Sha256 = "ABC", SizeBytes = 3 },
            ],
        };

        var json = ManifestSerializer.Serialize(manifest);
        var restored = ManifestSerializer.DeserializeSnapshot(json);
        Assert.Equal(manifest.ProjectId, restored.ProjectId);
        Assert.Equal("project.json", restored.Files[0].RelativePath);
    }
}
