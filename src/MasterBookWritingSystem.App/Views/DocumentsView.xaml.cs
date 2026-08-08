using System.Windows;
using System.Windows.Controls;
using MasterBookWritingSystem.App.ViewModels;

namespace MasterBookWritingSystem.App.Views;

public partial class DocumentsView : UserControl
{
    public DocumentsView()
    {
        InitializeComponent();
    }

    private void FieldEditor_GotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is DocumentsViewModel viewModel
            && sender is FrameworkElement { DataContext: DocumentFieldItemViewModel field })
        {
            viewModel.BeginFieldEdit(field);
        }
    }

    private void FieldEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is DocumentsViewModel viewModel
            && sender is FrameworkElement { DataContext: DocumentFieldItemViewModel field })
        {
            viewModel.EndFieldEdit(field);
        }
    }

    private void NotesEditor_GotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is DocumentsViewModel viewModel)
        {
            viewModel.BeginNotesEdit();
        }
    }

    private void NotesEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is DocumentsViewModel viewModel)
        {
            viewModel.EndNotesEdit();
        }
    }
}
