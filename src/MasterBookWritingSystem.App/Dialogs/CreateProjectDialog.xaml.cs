using System.Windows;

namespace MasterBookWritingSystem.App.Dialogs;

public partial class CreateProjectDialog : Window
{
    public CreateProjectDialog()
    {
        InitializeComponent();
        TitleBox.Focus();
    }

    public Services.CreateProjectDialogResult? Result { get; private set; }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show(this, "A project title is required.", "Create Project",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new Services.CreateProjectDialogResult
        {
            Title = TitleBox.Text.Trim(),
            Author = AuthorBox.Text.Trim(),
            Genre = GenreBox.Text.Trim(),
            NorthStar = NorthStarBox.Text.Trim(),
        };
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
