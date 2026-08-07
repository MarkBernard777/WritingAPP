using System.Windows;
using Microsoft.Win32;

namespace MasterBookWritingSystem.App.Services;

public sealed class WpfProjectDialogService : IProjectDialogService
{
    public string? PickFolder(string description)
    {
        var dialog = new OpenFolderDialog
        {
            Title = description,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public CreateProjectDialogResult? PromptCreateProject()
    {
        var window = new Dialogs.CreateProjectDialog
        {
            Owner = Application.Current?.MainWindow,
        };

        return window.ShowDialog() == true ? window.Result : null;
    }

    public void ShowMessage(string message, string caption)
        => MessageBox.Show(
            Application.Current?.MainWindow,
            message,
            caption,
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    public bool Confirm(string message, string caption)
        => MessageBox.Show(
            Application.Current?.MainWindow,
            message,
            caption,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public string? PickOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = false,
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
