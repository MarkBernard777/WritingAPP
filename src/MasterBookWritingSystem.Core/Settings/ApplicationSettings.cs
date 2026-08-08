using MasterBookWritingSystem.Core.Backup;

namespace MasterBookWritingSystem.Core.Settings;

public sealed class ApplicationSettings
{
    public int SnapshotRetentionLimit { get; set; } = SnapshotRetentionPolicy.DefaultMaxActiveSnapshots;
}
