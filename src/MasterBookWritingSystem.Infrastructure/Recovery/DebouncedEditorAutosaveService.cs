using MasterBookWritingSystem.Core.Abstractions;
using MasterBookWritingSystem.Core.Diagnostics;
using MasterBookWritingSystem.Core.Recovery;

namespace MasterBookWritingSystem.Infrastructure.Recovery;

public sealed class DebouncedEditorAutosaveService : IEditorAutosaveService, IDisposable
{
    private readonly IChapterService _chapters;
    private readonly IDocumentService _documents;
    private readonly ISaveStateService _saveState;
    private readonly Dictionary<Guid, PendingEdit> _pending = new();
    private readonly object _gate = new();
    private bool _disposed;

    public DebouncedEditorAutosaveService(
        IChapterService chapters,
        IDocumentService documents,
        ISaveStateService saveState)
    {
        _chapters = chapters;
        _documents = documents;
        _saveState = saveState;
    }

    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public bool HasPendingWork
    {
        get
        {
            lock (_gate)
            {
                return _pending.Values.Any(item => item.IsDirty || item.IsSaving);
            }
        }
    }

    public event EventHandler<EditorSaveCompletedEventArgs>? SaveCompleted;

    public void ScheduleManuscriptSave(Guid projectId, Guid chapterId, string markdown)
        => Schedule(
            RecoveryJournalIds.Create(projectId, RecoveryEntityType.ManuscriptChapter, chapterId),
            projectId,
            RecoveryEntityType.ManuscriptChapter,
            chapterId,
            secondaryKey: null,
            markdown ?? string.Empty);

    public void ScheduleDocumentFieldSave(Guid projectId, Guid documentId, string fieldKey, string value)
        => Schedule(
            RecoveryJournalIds.Create(projectId, RecoveryEntityType.DocumentField, documentId, fieldKey),
            projectId,
            RecoveryEntityType.DocumentField,
            documentId,
            fieldKey,
            value ?? string.Empty);

    public void ScheduleDocumentNotesSave(Guid projectId, Guid documentId, string notes)
        => Schedule(
            RecoveryJournalIds.Create(projectId, RecoveryEntityType.DocumentNotes, documentId),
            projectId,
            RecoveryEntityType.DocumentNotes,
            documentId,
            secondaryKey: null,
            notes ?? string.Empty);

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> slotIds;
        lock (_gate)
        {
            foreach (var edit in _pending.Values)
            {
                edit.Cts?.Cancel();
                edit.Cts = null;
            }

            slotIds = _pending.Values
                .Where(item => item.IsDirty)
                .Select(item => item.SlotId)
                .ToList();
        }

        foreach (var slotId in slotIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PendingEdit? live;
            long version;
            lock (_gate)
            {
                if (!_pending.TryGetValue(slotId, out live) || !live.IsDirty)
                {
                    continue;
                }

                version = live.Version;
            }

            await PersistAsync(live!, version, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public void CancelAll()
    {
        lock (_gate)
        {
            foreach (var edit in _pending.Values)
            {
                edit.Cts?.Cancel();
                edit.Cts = null;
                edit.Version++;
                edit.IsDirty = false;
                edit.IsSaving = false;
            }

            _pending.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelAll();
    }

    private void Schedule(
        Guid slotId,
        Guid projectId,
        RecoveryEntityType entityType,
        Guid entityId,
        string? secondaryKey,
        string draftText)
    {
        PendingEdit edit;
        long version;
        CancellationToken token;
        lock (_gate)
        {
            if (!_pending.TryGetValue(slotId, out edit!))
            {
                edit = new PendingEdit { SlotId = slotId };
                _pending[slotId] = edit;
            }

            edit.Cts?.Cancel();
            edit.Cts = new CancellationTokenSource();
            edit.Version++;
            version = edit.Version;
            edit.ProjectId = projectId;
            edit.EntityType = entityType;
            edit.EntityId = entityId;
            edit.SecondaryKey = secondaryKey;
            edit.DraftText = draftText;
            edit.IsDirty = true;
            token = edit.Cts.Token;
        }

        _ = DebounceAndSaveAsync(edit, version, token);
    }

    private async Task DebounceAndSaveAsync(PendingEdit edit, long version, CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceDelay, token).ConfigureAwait(false);
            await PersistAsync(edit, version, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded or cancelled — expected.
        }
        catch (Exception ex)
        {
            _saveState.Report(SaveState.SaveFailed, Truncate(ex.Message));
            RaiseCompleted(edit, edit.DraftText, succeeded: false, ex.Message);
        }
    }

    private async Task PersistAsync(PendingEdit edit, long version, CancellationToken cancellationToken)
    {
        string draft;
        Guid projectId;
        RecoveryEntityType entityType;
        Guid entityId;
        string? secondaryKey;
        lock (_gate)
        {
            if (edit.Version != version)
            {
                return;
            }

            draft = edit.DraftText;
            projectId = edit.ProjectId;
            entityType = edit.EntityType;
            entityId = edit.EntityId;
            secondaryKey = edit.SecondaryKey;
            edit.IsSaving = true;
        }

        _saveState.Report(SaveState.Saving, "Saving…");

        try
        {
            switch (entityType)
            {
                case RecoveryEntityType.ManuscriptChapter:
                    await _chapters
                        .SaveContentAsync(projectId, entityId, draft, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case RecoveryEntityType.DocumentField:
                    await _documents
                        .UpdateFieldAsync(projectId, entityId, secondaryKey!, draft, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case RecoveryEntityType.DocumentNotes:
                    await _documents
                        .UpdateNotesAsync(projectId, entityId, draft, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }

            lock (_gate)
            {
                if (edit.Version == version)
                {
                    edit.IsDirty = false;
                }

                edit.IsSaving = false;
            }

            await _saveState.RefreshRecoveryAvailabilityAsync(projectId, CancellationToken.None)
                .ConfigureAwait(false);
            if (_saveState.State != SaveState.RecoveryAvailable)
            {
                _saveState.Report(SaveState.Saved, "Saved");
            }

            RaiseCompleted(edit, draft, succeeded: true, error: null);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                edit.IsSaving = false;
            }

            throw;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                edit.IsSaving = false;
                edit.IsDirty = true;
            }

            _saveState.Report(SaveState.SaveFailed, Truncate(ex.Message));
            RaiseCompleted(edit, draft, succeeded: false, ex.Message);
        }
    }

    private void RaiseCompleted(PendingEdit edit, string savedText, bool succeeded, string? error)
    {
        SaveCompleted?.Invoke(this, new EditorSaveCompletedEventArgs
        {
            ProjectId = edit.ProjectId,
            EntityType = edit.EntityType,
            EntityId = edit.EntityId,
            SecondaryKey = edit.SecondaryKey,
            SavedText = savedText,
            Succeeded = succeeded,
            ErrorMessage = error,
        });
    }

    private static string Truncate(string message)
        => DiagnosticTextSanitizer.Sanitize(message, maxLength: 160);

    private sealed class PendingEdit
    {
        public Guid SlotId { get; init; }

        public Guid ProjectId { get; set; }

        public RecoveryEntityType EntityType { get; set; }

        public Guid EntityId { get; set; }

        public string? SecondaryKey { get; set; }

        public string DraftText { get; set; } = string.Empty;

        public long Version { get; set; }

        public bool IsDirty { get; set; }

        public bool IsSaving { get; set; }

        public CancellationTokenSource? Cts { get; set; }
    }
}
