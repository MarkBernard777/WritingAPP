using System.IO;
using System.Text.Json;
using MasterBookWritingSystem.Core.Abstractions;

namespace MasterBookWritingSystem.App.Services;

public interface IRecentProjectsStore
{
    Task<IReadOnlyList<string>> GetRecentAsync(CancellationToken cancellationToken = default);

    Task AddAsync(string projectRootPath, CancellationToken cancellationToken = default);
}

public sealed class RecentProjectsStore : IRecentProjectsStore
{
    private readonly IApplicationPaths _paths;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public RecentProjectsStore(IApplicationPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<string>> GetRecentAsync(CancellationToken cancellationToken = default)
    {
        var file = GetFilePath();
        if (!File.Exists(file))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task AddAsync(string projectRootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        Directory.CreateDirectory(_paths.LocalAppDataDirectory);

        var recent = (await GetRecentAsync(cancellationToken).ConfigureAwait(false))
            .Where(path => !string.Equals(path, projectRootPath, StringComparison.OrdinalIgnoreCase))
            .Prepend(Path.GetFullPath(projectRootPath))
            .Take(10)
            .ToList();

        var json = JsonSerializer.Serialize(recent, JsonOptions);
        await File.WriteAllTextAsync(GetFilePath(), json, cancellationToken).ConfigureAwait(false);
    }

    private string GetFilePath()
        => Path.Combine(_paths.LocalAppDataDirectory, "recent-projects.json");
}
