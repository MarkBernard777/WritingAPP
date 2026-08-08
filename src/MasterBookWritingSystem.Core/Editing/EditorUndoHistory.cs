namespace MasterBookWritingSystem.Core.Editing;

public sealed class EditorUndoHistory<T>
{
    public const int DefaultMaxDepth = 100;

    private readonly int _maxDepth;
    private readonly List<T> _undo = [];
    private readonly List<T> _redo = [];

    public EditorUndoHistory(int maxDepth = DefaultMaxDepth)
    {
        if (maxDepth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Undo history depth must be at least 1.");
        }

        _maxDepth = maxDepth;
    }

    public int MaxDepth => _maxDepth;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public void Push(T unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _undo.Add(unit);
        while (_undo.Count > _maxDepth)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    public bool TryPeekUndo(out T? unit)
    {
        if (_undo.Count == 0)
        {
            unit = default;
            return false;
        }

        unit = _undo[^1];
        return true;
    }

    public bool TryUndo(out T? unit)
    {
        if (_undo.Count == 0)
        {
            unit = default;
            return false;
        }

        unit = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(unit);
        return true;
    }

    public bool TryRedo(out T? unit)
    {
        if (_redo.Count == 0)
        {
            unit = default;
            return false;
        }

        unit = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(unit);
        while (_undo.Count > _maxDepth)
        {
            _undo.RemoveAt(0);
        }

        return true;
    }

    public void ReplaceTop(T unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_undo.Count == 0)
        {
            Push(unit);
            return;
        }

        _undo[^1] = unit;
        _redo.Clear();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
