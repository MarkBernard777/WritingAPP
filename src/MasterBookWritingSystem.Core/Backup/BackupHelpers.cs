using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterBookWritingSystem.Core.Backup;

namespace MasterBookWritingSystem.Core.Backup;

public static class BackupPathRules
{
    public static readonly string[] ExcludedDirectoryNames =
    [
        "Exports",
        "Snapshots",
        "ManuscriptVersions",
        "SearchReplaceRollback",
        "Recovery",
        ".tmp",
        "tmp",
        "Temp",
        "cache",
        "Cache",
    ];

    public static bool IsExcludedRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (normalized.StartsWith("Exports/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Exports", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains("/Snapshots/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("13 Archive/Snapshots", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/Snapshots", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains("/Recovery/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("13 Archive/Recovery", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/Recovery", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains("/ManuscriptVersions/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("13 Archive/ManuscriptVersions", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/ManuscriptVersions", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains("/SearchReplaceRollback/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("13 Archive/SearchReplaceRollback", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("/SearchReplaceRollback", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment =>
            ExcludedDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase)
            || segment.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    public static string SnapshotsRelativeDirectory
        => Path.Combine("13 Archive", "Snapshots").Replace('\\', '/');
}

public static class ChecksumHelper
{
    public static async Task<string> Sha256FileAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    public static string Sha256Bytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));
}

public static class ManifestSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(SnapshotManifest manifest)
        => JsonSerializer.Serialize(manifest, JsonOptions);

    public static SnapshotManifest DeserializeSnapshot(string json)
        => JsonSerializer.Deserialize<SnapshotManifest>(json, JsonOptions)
           ?? throw new InvalidOperationException("Snapshot manifest was empty.");

    public static string Serialize(PortablePackageManifest manifest)
        => JsonSerializer.Serialize(manifest, JsonOptions);

    public static PortablePackageManifest DeserializePortable(string json)
        => JsonSerializer.Deserialize<PortablePackageManifest>(json, JsonOptions)
           ?? throw new InvalidOperationException("Portable package manifest was empty.");
}

public static class ExportFileNames
{
    public static string Timestamped(string prefix, string extension)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return $"{prefix}-{stamp}{ext}";
    }
}
