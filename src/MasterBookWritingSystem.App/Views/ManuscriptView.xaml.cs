using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MasterBookWritingSystem.App.ViewModels;

namespace MasterBookWritingSystem.App.Views;

public partial class ManuscriptView : UserControl
{
    private ManuscriptViewModel? _boundViewModel;

    public ManuscriptView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => DetachViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        if (e.NewValue is ManuscriptViewModel viewModel)
        {
            _boundViewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdatePreview(viewModel.PreviewHtml);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManuscriptViewModel.PreviewHtml) && _boundViewModel is not null)
        {
            UpdatePreview(_boundViewModel.PreviewHtml);
        }
    }

    private void UpdatePreview(string html)
    {
        try
        {
            PreviewBrowser.NavigateToString(string.IsNullOrWhiteSpace(html)
                ? "<html><body></body></html>"
                : html);
        }
        catch
        {
            // WebBrowser can throw if the control is not ready; ignore transient failures.
        }
    }

    private void DetachViewModel()
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel = null;
        }
    }
}
