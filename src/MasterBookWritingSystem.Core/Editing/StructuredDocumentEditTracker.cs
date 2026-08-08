namespace MasterBookWritingSystem.Core.Editing;

/// <summary>
/// Tracks structured document field/notes values with bounded undo/redo.
/// Applying undo/redo updates in-memory state; the host schedules autosave for the restored values.
/// </summary>
public sealed class StructuredDocumentEditTracker
{
    private readonly StructuredDocumentUndoSession _session;
    private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);
    private string _notes = string.Empty;
    private bool _suppressRecording;

    public StructuredDocumentEditTracker(int maxDepth = EditorUndoHistory<object>.DefaultMaxDepth)
    {
        _session = new StructuredDocumentUndoSession(maxDepth);
    }

    public bool CanUndo => _session.CanUndo;

    public bool CanRedo => _session.CanRedo;

    public int UndoCount => _session.UndoCount;

    public string Notes => _notes;

    public IReadOnlyDictionary<string, string> Fields => _fields;

    public void Reset(IEnumerable<KeyValuePair<string, string>> fields, string notes)
    {
        _fields.Clear();
        foreach (var pair in fields)
        {
            _fields[pair.Key] = pair.Value ?? string.Empty;
        }

        _notes = notes ?? string.Empty;
        _session.Clear();
    }

    public void ClearHistory() => _session.Clear();

    public bool SetField(string fieldKey, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        value ??= string.Empty;
        _fields.TryGetValue(fieldKey, out var previous);
        previous ??= string.Empty;
        if (string.Equals(previous, value, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_suppressRecording)
        {
            _session.RecordFieldChange(fieldKey, previous, value);
        }

        _fields[fieldKey] = value;
        return true;
    }

    public bool SetNotes(string value)
    {
        value ??= string.Empty;
        if (string.Equals(_notes, value, StringComparison.Ordinal))
        {
            return false;
        }

        if (!_suppressRecording)
        {
            _session.RecordNotesChange(_notes, value);
        }

        _notes = value;
        return true;
    }

    public bool TryUndo(out StructuredEditApplication? application)
    {
        if (!_session.TryUndo(out var unit) || unit is null)
        {
            application = null;
            return false;
        }

        application = ApplyUnit(unit, undo: true);
        return true;
    }

    public bool TryRedo(out StructuredEditApplication? application)
    {
        if (!_session.TryRedo(out var unit) || unit is null)
        {
            application = null;
            return false;
        }

        application = ApplyUnit(unit, undo: false);
        return true;
    }

    private StructuredEditApplication ApplyUnit(StructuredEditUnit unit, bool undo)
    {
        _suppressRecording = true;
        try
        {
            switch (unit)
            {
                case FieldEditUnit field:
                    var fieldValue = undo ? field.OldValue : field.NewValue;
                    _fields[field.FieldKey] = fieldValue;
                    return new StructuredEditApplication(
                        StructuredEditTarget.Field,
                        field.FieldKey,
                        fieldValue);
                case NotesEditUnit notes:
                    var notesValue = undo ? notes.OldValue : notes.NewValue;
                    _notes = notesValue;
                    return new StructuredEditApplication(
                        StructuredEditTarget.Notes,
                        FieldKey: null,
                        notesValue);
                default:
                    throw new InvalidOperationException($"Unknown undo unit '{unit.GetType().Name}'.");
            }
        }
        finally
        {
            _suppressRecording = false;
        }
    }
}

public enum StructuredEditTarget
{
    Field,
    Notes,
}

public sealed record StructuredEditApplication(
    StructuredEditTarget Target,
    string? FieldKey,
    string Value);
