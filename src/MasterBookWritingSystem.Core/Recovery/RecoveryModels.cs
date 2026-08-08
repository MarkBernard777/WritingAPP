namespace MasterBookWritingSystem.Core.Recovery;

public enum SaveState
{
    Clean = 0,
    Saving = 1,
    Saved = 2,
    SaveFailed = 3,
    RecoveryAvailable = 4,
}

public enum RecoveryEntityType
{
    ManuscriptChapter = 0,
    DocumentField = 1,
    DocumentNotes = 2,
}

public sealed class RecoveryJournalEntry
{
    public const int CurrentFormatVersion = 1;

    public Guid EntryId { get; set; }

    public Guid ProjectId { get; set; }

    public RecoveryEntityType EntityType { get; set; }

    /// <summary>Chapter id or working-document id.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Field key for document fields; otherwise null.</summary>
    public string? SecondaryKey { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public string DraftText { get; set; } = string.Empty;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
}

public sealed class RecoveryJournalInfo
{
    public required Guid EntryId { get; init; }

    public required Guid ProjectId { get; init; }

    public required RecoveryEntityType EntityType { get; init; }

    public required Guid EntityId { get; init; }

    public string? SecondaryKey { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required DateTimeOffset UpdatedUtc { get; init; }

    public required string AbsolutePath { get; init; }

    public required int DraftLength { get; init; }
}

public static class RecoveryJournalIds
{
    public static Guid Create(
        Guid projectId,
        RecoveryEntityType entityType,
        Guid entityId,
        string? secondaryKey = null)
    {
        var material = $"{projectId:N}|{(int)entityType}|{entityId:N}|{secondaryKey ?? string.Empty}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
        return new Guid(hash.AsSpan(0, 16));
    }
}

public static class RecoveryPaths
{
    public const string RecoveryDirectoryName = "Recovery";

    public const string JournalDirectoryName = "Journal";

    public static string RelativeJournalDirectory
        => Path.Combine("13 Archive", RecoveryDirectoryName, JournalDirectoryName).Replace('\\', '/');

    public static string GetJournalDirectory(string projectRootPath)
        => Path.Combine(
            Path.GetFullPath(projectRootPath),
            "13 Archive",
            RecoveryDirectoryName,
            JournalDirectoryName);
}
