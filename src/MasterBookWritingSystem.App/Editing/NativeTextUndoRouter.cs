using System.Windows;
using System.Windows.Input;

namespace MasterBookWritingSystem.App.Editing;

/// <summary>
/// Routes undo/redo to the focused WPF text control when its native stack can execute.
/// </summary>
public static class NativeTextUndoRouter
{
    public static bool CanUndo()
    {
        var target = Keyboard.FocusedElement as IInputElement;
        return target is not null && ApplicationCommands.Undo.CanExecute(null, target);
    }

    public static bool CanRedo()
    {
        var target = Keyboard.FocusedElement as IInputElement;
        return target is not null && ApplicationCommands.Redo.CanExecute(null, target);
    }

    public static bool TryUndo()
    {
        var target = Keyboard.FocusedElement as IInputElement;
        if (target is null || !ApplicationCommands.Undo.CanExecute(null, target))
        {
            return false;
        }

        ApplicationCommands.Undo.Execute(null, target);
        return true;
    }

    public static bool TryRedo()
    {
        var target = Keyboard.FocusedElement as IInputElement;
        if (target is null || !ApplicationCommands.Redo.CanExecute(null, target))
        {
            return false;
        }

        ApplicationCommands.Redo.Execute(null, target);
        return true;
    }
}
