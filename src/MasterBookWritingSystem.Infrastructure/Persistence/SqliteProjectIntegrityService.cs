using MasterBookWritingSystem.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace MasterBookWritingSystem.Infrastructure.Persistence;

public sealed class SqliteProjectIntegrityService : IProjectIntegrityService
{
    private readonly TimeProvider _timeProvider;

    public SqliteProjectIntegrityService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<ProjectIntegrityResult> CheckAsync(
        string projectRootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);

        var root = Path.GetFullPath(projectRootPath);
        var databasePath = Path.Combine(root, ProjectPaths.DatabaseFileName);
        var checkedUtc = _timeProvider.GetUtcNow();
        if (!File.Exists(databasePath))
        {
            return new ProjectIntegrityResult
            {
                IsHealthy = false,
                Summary = $"Missing {ProjectPaths.DatabaseFileName}.",
                CheckedUtc = checkedUtc,
            };
        }

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";

            var details = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                details.Add(reader.GetString(0));
            }

            var healthy = details.Count == 1
                && details[0].Equals("ok", StringComparison.OrdinalIgnoreCase);
            return new ProjectIntegrityResult
            {
                IsHealthy = healthy,
                Summary = healthy
                    ? "SQLite integrity check passed."
                    : "SQLite integrity check reported database damage.",
                CheckedUtc = checkedUtc,
                Details = healthy ? [] : details,
            };
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            return new ProjectIntegrityResult
            {
                IsHealthy = false,
                Summary = "SQLite integrity check could not be completed.",
                CheckedUtc = checkedUtc,
                Details = [ex.Message],
            };
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }
}
