using System.ComponentModel;
using System.Windows;
using MasterBookWritingSystem.App.ViewModels;

namespace MasterBookWritingSystem.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private bool _exitFlushCompleted;

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_exitFlushCompleted)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        await _viewModel.FlushPendingSavesAsync().ConfigureAwait(true);
        _exitFlushCompleted = true;
        Close();
    }
}
