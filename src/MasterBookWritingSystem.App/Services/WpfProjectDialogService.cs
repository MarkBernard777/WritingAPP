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
}
