using System.Text.Json;
using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Recovery;
using MasterBookWritingSystem.Infrastructure.IO;

namespace MasterBookWritingSystem.Infrastructure.Recovery;

public sealed class FileRecoveryJournalService : IRecoveryJournalService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IProjectService _projects;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileRecoveryJournalService(IProjectService projects, TimeProvider timeProvider)
    {
        _projects = projects;
        _timeProvider = timeProvider;
    }

    public async Task UpsertAsync(RecoveryJournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.EntryId == Guid.Empty)
        {
            entry.EntryId = RecoveryJournalIds.Create(
                entry.ProjectId,
                entry.EntityType,
                entry.EntityId,
                entry.SecondaryKey);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = RequireProjectRoot(entry.ProjectId);
            var directory = RecoveryPaths.GetJournalDirectory(root);
            Directory.CreateDirectory(directory);

            var path = GetEntryPath(directory, entry.EntryId);
            var now = _timeProvider.GetUtcNow();
            RecoveryJournalEntry toWrite;
            if (File.Exists(path))
            {
                var existing = await ReadEntryAsync(path, cancellationToken).ConfigureAwait(false);
                toWrite = existing ?? entry;
                toWrite.EntryId = entry.EntryId;
                toWrite.ProjectId = entry.ProjectId;
                toWrite.EntityType = entry.EntityType;
                toWrite.EntityId = entry.EntityId;
                toWrite.SecondaryKey = entry.SecondaryKey;
                toWrite.DraftText = entry.DraftText ?? string.Empty;
                toWrite.UpdatedUtc = now;
                toWrite.FormatVersion = RecoveryJournalEntry.CurrentFormatVersion;
                if (toWrite.CreatedUtc == default)
                {
                    toWrite.CreatedUtc = existing?.CreatedUtc ?? now;
                }
            }
            else
            {
                toWrite = new RecoveryJournalEntry
                {
                    EntryId = entry.EntryId,
                    ProjectId = entry.ProjectId,
                    EntityType = entry.EntityType,
                    EntityId = entry.EntityId,
                    SecondaryKey = entry.SecondaryKey,
                    DraftText = entry.DraftText ?? string.Empty,
                    CreatedUtc = now,
                    UpdatedUtc = now,
                    FormatVersion = RecoveryJournalEntry.CurrentFormatVersion,
                };
            }

            var json = JsonSerializer.Serialize(toWrite, JsonOptions);
            await AtomicFileWriter.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(Guid projectId, Guid entryId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = RequireProjectRoot(projectId);
            var path = GetEntryPath(RecoveryPaths.GetJournalDirectory(root), entryId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecoveryJournalEntry?> GetAsync(
        Guid projectId,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = RequireProjectRoot(projectId);
            var path = GetEntryPath(RecoveryPaths.GetJournalDirectory(root), entryId);
            if (!File.Exists(path))
            {
                return null;
            }

            return await ReadEntryAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<RecoveryJournalInfo>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = RequireProjectRoot(projectId);
            var directory = RecoveryPaths.GetJournalDirectory(root);
            if (!Directory.Exists(directory))
            {
                return [];
            }

            var results = new List<RecoveryJournalInfo>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = await ReadEntryAsync(path, cancellationToken).ConfigureAwait(false);
                if (entry is null || entry.ProjectId != projectId)
                {
                    continue;
                }

                results.Add(new RecoveryJournalInfo
                {
                    EntryId = entry.EntryId,
                    ProjectId = entry.ProjectId,
                    EntityType = entry.EntityType,
                    EntityId = entry.EntityId,
                    SecondaryKey = entry.SecondaryKey,
                    CreatedUtc = entry.CreatedUtc,
                    UpdatedUtc = entry.UpdatedUtc,
                    AbsolutePath = path,
                    DraftLength = entry.DraftText.Length,
                });
            }

            return results
                .OrderByDescending(item => item.UpdatedUtc)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> HasRecoverableEntriesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var list = await ListAsync(projectId, cancellationToken).ConfigureAwait(false);
        return list.Count > 0;
    }

    private string RequireProjectRoot(Guid projectId)
    {
        var project = _projects.ActiveProject
            ?? throw new InvalidOperationException("Open a project before using the recovery journal.");
        if (project.Id != projectId)
        {
            throw new InvalidOperationException("Recovery journal project does not match the active project.");
        }

        return project.RootPath;
    }

    private static string GetEntryPath(string directory, Guid entryId)
        => Path.Combine(directory, $"{entryId:N}.json");

    private static async Task<RecoveryJournalEntry?> ReadEntryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<RecoveryJournalEntry>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
