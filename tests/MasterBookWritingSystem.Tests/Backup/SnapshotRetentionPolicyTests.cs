using MasterBookWritingSystem.Core.Backup;

namespace MasterBookWritingSystem.Tests.Backup;

public sealed class SnapshotRetentionPolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(200)]
    public void Normalize_AcceptsValidLimits(int limit)
    {
        Assert.Equal(limit, SnapshotRetentionPolicy.Normalize(limit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public void Normalize_RejectsOutOfRange(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SnapshotRetentionPolicy.Normalize(limit));
        Assert.False(SnapshotRetentionPolicy.TryNormalize(limit, out _, out var error));
        Assert.Contains("between", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultAndRetiredNames_AreStable()
    {
        Assert.Equal(20, SnapshotRetentionPolicy.DefaultMaxActiveSnapshots);
        Assert.Equal("Retired", SnapshotRetentionPolicy.RetiredDirectoryName);
        Assert.Equal("13 Archive/Snapshots", SnapshotRetentionPolicy.ActiveSnapshotsRelativeDirectory);
        Assert.Equal("13 Archive/Snapshots/Retired", SnapshotRetentionPolicy.RetiredSnapshotsRelativeDirectory);
        Assert.True(SnapshotRetentionPolicy.IsRetiredDirectoryName("Retired"));
        Assert.False(SnapshotRetentionPolicy.IsRetiredDirectoryName("auto-20260101"));
    }
}
