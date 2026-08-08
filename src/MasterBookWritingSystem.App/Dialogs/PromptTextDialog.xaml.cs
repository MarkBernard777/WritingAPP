using System.Windows;

namespace MasterBookWritingSystem.App.Dialogs;

public partial class PromptTextDialog : Window
{
    public PromptTextDialog(string title, string prompt, string? initialValue)
    {
        InitializeComponent();
        Title = title;
        Prompt = prompt;
        Value = initialValue ?? string.Empty;
        DataContext = this;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public string Prompt { get; }

    public string Value { get; set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            return;
        }

        DialogResult = true;
    }
}
