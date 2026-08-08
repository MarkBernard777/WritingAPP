namespace MasterBookWritingSystem.Core.Editing;

public abstract record StructuredEditUnit;

public sealed record FieldEditUnit(string FieldKey, string OldValue, string NewValue) : StructuredEditUnit;

public sealed record NotesEditUnit(string OldValue, string NewValue) : StructuredEditUnit;

/// <summary>
/// Model-level undo for structured documents. Consecutive edits to the same field/notes
/// are coalesced so typing bursts become one undo step, while switching fields creates
/// separate units for cross-value undo.
/// </summary>
public sealed class StructuredDocumentUndoSession
{
    private readonly EditorUndoHistory<StructuredEditUnit> _history;

    public StructuredDocumentUndoSession(int maxDepth = EditorUndoHistory<object>.DefaultMaxDepth)
    {
        _history = new EditorUndoHistory<StructuredEditUnit>(maxDepth);
    }

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public int UndoCount => _history.UndoCount;

    public void Clear() => _history.Clear();

    public void RecordFieldChange(string fieldKey, string oldValue, string newValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        oldValue ??= string.Empty;
        newValue ??= string.Empty;
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        if (_history.TryPeekUndo(out var top)
            && top is FieldEditUnit field
            && string.Equals(field.FieldKey, fieldKey, StringComparison.Ordinal))
        {
            _history.ReplaceTop(field with { NewValue = newValue });
            return;
        }

        _history.Push(new FieldEditUnit(fieldKey, oldValue, newValue));
    }

    public void RecordNotesChange(string oldValue, string newValue)
    {
        oldValue ??= string.Empty;
        newValue ??= string.Empty;
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        if (_history.TryPeekUndo(out var top) && top is NotesEditUnit)
        {
            var notes = (NotesEditUnit)top!;
            _history.ReplaceTop(notes with { NewValue = newValue });
            return;
        }

        _history.Push(new NotesEditUnit(oldValue, newValue));
    }

    public bool TryUndo(out StructuredEditUnit? unit) => _history.TryUndo(out unit);

    public bool TryRedo(out StructuredEditUnit? unit) => _history.TryRedo(out unit);
}
