using MasterBookWritingSystem.Core.Recovery;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IRecoveryJournalService
{
    Task UpsertAsync(RecoveryJournalEntry entry, CancellationToken cancellationToken = default);

    Task ClearAsync(Guid projectId, Guid entryId, CancellationToken cancellationToken = default);

    Task<RecoveryJournalEntry?> GetAsync(
        Guid projectId,
        Guid entryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecoveryJournalInfo>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<bool> HasRecoverableEntriesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

public interface ISaveStateService
{
    SaveState State { get; }

    string Message { get; }

    DateTimeOffset? LastSavedUtc { get; }

    event EventHandler? Changed;

    void Report(SaveState state, string? message = null);

    Task RefreshRecoveryAvailabilityAsync(Guid? projectId, CancellationToken cancellationToken = default);
}

public interface IEditorAutosaveService
{
    TimeSpan DebounceDelay { get; set; }

    bool HasPendingWork { get; }

    event EventHandler<EditorSaveCompletedEventArgs>? SaveCompleted;

    void ScheduleManuscriptSave(Guid projectId, Guid chapterId, string markdown);

    void ScheduleDocumentFieldSave(Guid projectId, Guid documentId, string fieldKey, string value);

    void ScheduleDocumentNotesSave(Guid projectId, Guid documentId, string notes);

    Task FlushAsync(CancellationToken cancellationToken = default);

    void CancelAll();
}

public sealed class EditorSaveCompletedEventArgs : EventArgs
{
    public required Guid ProjectId { get; init; }

    public required RecoveryEntityType EntityType { get; init; }

    public required Guid EntityId { get; init; }

    public string? SecondaryKey { get; init; }

    public string SavedText { get; init; } = string.Empty;

    public required bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }
}
