using System.Text.Json;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Backup;
using MasterBookWritingSystem.Core.Settings;
using MasterBookWritingSystem.Infrastructure.IO;

namespace MasterBookWritingSystem.Infrastructure.Settings;

public sealed class JsonApplicationSettingsStore : IApplicationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IApplicationPaths _paths;
    private readonly object _gate = new();

    public JsonApplicationSettingsStore(IApplicationPaths paths)
    {
        _paths = paths;
    }

    public ApplicationSettings GetSettings()
    {
        lock (_gate)
        {
            var file = GetFilePath();
            if (!File.Exists(file))
            {
                return new ApplicationSettings();
            }

            try
            {
                var json = File.ReadAllText(file);
                var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, JsonOptions)
                    ?? new ApplicationSettings();
                if (!SnapshotRetentionPolicy.TryNormalize(
                        settings.SnapshotRetentionLimit,
                        out var normalized,
                        out _))
                {
                    settings.SnapshotRetentionLimit = SnapshotRetentionPolicy.DefaultMaxActiveSnapshots;
                }
                else
                {
                    settings.SnapshotRetentionLimit = normalized;
                }

                return settings;
            }
            catch
            {
                return new ApplicationSettings();
            }
        }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = SnapshotRetentionPolicy.Normalize(settings.SnapshotRetentionLimit);
        var toSave = new ApplicationSettings { SnapshotRetentionLimit = normalized };

        Directory.CreateDirectory(_paths.LocalAppDataDirectory);
        var json = JsonSerializer.Serialize(toSave, JsonOptions);
        await AtomicFileWriter.WriteAllTextAsync(GetFilePath(), json, cancellationToken).ConfigureAwait(false);
    }

    private string GetFilePath()
        => Path.Combine(_paths.LocalAppDataDirectory, "application-settings.json");
}
