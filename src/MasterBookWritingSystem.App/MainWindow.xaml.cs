using System.Windows;
using MasterBookWritingSystem.App.ViewModels;

namespace MasterBookWritingSystem.App;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
