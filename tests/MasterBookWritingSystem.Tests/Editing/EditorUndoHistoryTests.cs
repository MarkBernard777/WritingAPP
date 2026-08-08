using MasterBookWritingSystem.Core.Editing;

namespace MasterBookWritingSystem.Tests.Editing;

public sealed class StructuredDocumentEditTrackerTests
{
    [Fact]
    public void UndoRedo_RestoresFieldAndNotes_WithoutResurrectingClearedHistory()
    {
        var tracker = new StructuredDocumentEditTracker();
        tracker.Reset(
            [new KeyValuePair<string, string>("goal", ""), new KeyValuePair<string, string>("need", "")],
            notes: "");

        tracker.SetField("goal", "save the village");
        tracker.SetField("need", "allies");
        tracker.SetNotes("draft notes");

        Assert.True(tracker.TryUndo(out var notesUndo));
        Assert.Equal(StructuredEditTarget.Notes, notesUndo!.Target);
        Assert.Equal(string.Empty, notesUndo.Value);
        Assert.Equal(string.Empty, tracker.Notes);

        Assert.True(tracker.TryUndo(out var needUndo));
        Assert.Equal("need", needUndo!.FieldKey);
        Assert.Equal(string.Empty, needUndo.Value);

        Assert.True(tracker.TryRedo(out var needRedo));
        Assert.Equal("allies", needRedo!.Value);

        tracker.SetField("need", "new allies");
        Assert.False(tracker.CanRedo);
    }

    [Fact]
    public void SwitchingDocument_Reset_ClearsHistory()
    {
        var tracker = new StructuredDocumentEditTracker();
        tracker.Reset([new KeyValuePair<string, string>("a", "1")], "");
        tracker.SetField("a", "2");
        Assert.True(tracker.CanUndo);

        tracker.Reset([new KeyValuePair<string, string>("b", "x")], "notes");
        Assert.False(tracker.CanUndo);
        Assert.False(tracker.CanRedo);
        Assert.Equal("x", tracker.Fields["b"]);
    }

    [Fact]
    public void Undo_ProducesNewestStateForAutosaveHost()
    {
        var scheduled = new List<(string Target, string Value)>();
        var tracker = new StructuredDocumentEditTracker();
        tracker.Reset([new KeyValuePair<string, string>("hook", "old")], "");
        tracker.SetField("hook", "new");

        Assert.True(tracker.TryUndo(out var app));
        scheduled.Add(("field:" + app!.FieldKey, app.Value));

        Assert.Equal(("field:hook", "old"), scheduled[0]);
        Assert.Equal("old", tracker.Fields["hook"]);
    }

    [Fact]
    public void History_RespectsDefaultBoundOf100()
    {
        var tracker = new StructuredDocumentEditTracker();
        var fields = Enumerable.Range(0, 120)
            .Select(i => new KeyValuePair<string, string>($"f{i}", ""))
            .ToArray();
        tracker.Reset(fields, "");

        for (var i = 0; i < 120; i++)
        {
            tracker.SetField($"f{i}", "v");
        }

        Assert.Equal(EditorUndoHistory<object>.DefaultMaxDepth, tracker.UndoCount);
    }
}


public sealed class EditorUndoHistoryTests
{
    [Fact]
    public void UndoRedo_RoundTrips_AndNewEditInvalidatesRedo()
    {
        var history = new EditorUndoHistory<string>();
        history.Push("a");
        history.Push("b");

        Assert.True(history.TryUndo(out var first));
        Assert.Equal("b", first);
        Assert.True(history.CanRedo);

        Assert.True(history.TryRedo(out var redone));
        Assert.Equal("b", redone);

        Assert.True(history.TryUndo(out _));
        history.Push("c");
        Assert.False(history.CanRedo);
        Assert.True(history.TryUndo(out var afterNew));
        Assert.Equal("c", afterNew);
    }

    [Fact]
    public void History_IsBounded_ToMaxDepth()
    {
        var history = new EditorUndoHistory<int>(maxDepth: 3);
        history.Push(1);
        history.Push(2);
        history.Push(3);
        history.Push(4);

        Assert.Equal(3, history.UndoCount);
        Assert.True(history.TryUndo(out var newest));
        Assert.Equal(4, newest);
        Assert.True(history.TryUndo(out var mid));
        Assert.Equal(3, mid);
        Assert.True(history.TryUndo(out var oldestKept));
        Assert.Equal(2, oldestKept);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Clear_RemovesUndoAndRedo()
    {
        var history = new EditorUndoHistory<string>();
        history.Push("x");
        history.TryUndo(out _);
        history.Clear();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }
}

public sealed class StructuredDocumentUndoSessionTests
{
    [Fact]
    public void CoalescesConsecutiveEditsToSameField_ButSplitsAcrossFields()
    {
        var session = new StructuredDocumentUndoSession();
        session.RecordFieldChange("goal", "", "g");
        session.RecordFieldChange("goal", "g", "go");
        session.RecordFieldChange("goal", "go", "goal");
        session.RecordFieldChange("need", "", "n");

        Assert.Equal(2, session.UndoCount);
        Assert.True(session.TryUndo(out var need));
        Assert.Equal(new FieldEditUnit("need", "", "n"), need);
        Assert.True(session.TryUndo(out var goal));
        Assert.Equal(new FieldEditUnit("goal", "", "goal"), goal);
    }

    [Fact]
    public void NotesEdits_Coalesce_AndRedoInvalidatesAfterNewEdit()
    {
        var session = new StructuredDocumentUndoSession();
        session.RecordNotesChange("", "a");
        session.RecordNotesChange("a", "ab");
        Assert.Equal(1, session.UndoCount);

        Assert.True(session.TryUndo(out var unit));
        Assert.Equal(new NotesEditUnit("", "ab"), unit);
        Assert.True(session.CanRedo);

        session.RecordNotesChange("", "fresh");
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Clear_ResetsSession()
    {
        var session = new StructuredDocumentUndoSession();
        session.RecordFieldChange("a", "1", "2");
        session.Clear();
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }
}
