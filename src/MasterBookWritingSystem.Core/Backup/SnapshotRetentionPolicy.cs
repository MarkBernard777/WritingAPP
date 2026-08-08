namespace MasterBookWritingSystem.Core.Backup;

public static class SnapshotRetentionPolicy
{
    public const int DefaultMaxActiveSnapshots = 20;

    public const int MinimumMaxActiveSnapshots = 1;

    public const int MaximumMaxActiveSnapshots = 200;

    public const string RetiredDirectoryName = "Retired";

    public static string ActiveSnapshotsRelativeDirectory
        => Path.Combine("13 Archive", "Snapshots").Replace('\\', '/');

    public static string RetiredSnapshotsRelativeDirectory
        => Path.Combine("13 Archive", "Snapshots", RetiredDirectoryName).Replace('\\', '/');

    public static int Normalize(int requestedLimit)
    {
        if (requestedLimit < MinimumMaxActiveSnapshots || requestedLimit > MaximumMaxActiveSnapshots)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedLimit),
                requestedLimit,
                $"Snapshot retention must be between {MinimumMaxActiveSnapshots} and {MaximumMaxActiveSnapshots}.");
        }

        return requestedLimit;
    }

    public static bool TryNormalize(int requestedLimit, out int normalized, out string? error)
    {
        if (requestedLimit < MinimumMaxActiveSnapshots || requestedLimit > MaximumMaxActiveSnapshots)
        {
            normalized = DefaultMaxActiveSnapshots;
            error =
                $"Snapshot retention must be between {MinimumMaxActiveSnapshots} and {MaximumMaxActiveSnapshots}.";
            return false;
        }

        normalized = requestedLimit;
        error = null;
        return true;
    }

    public static bool IsRetiredDirectoryName(string directoryName)
        => string.Equals(directoryName, RetiredDirectoryName, StringComparison.OrdinalIgnoreCase);
}
