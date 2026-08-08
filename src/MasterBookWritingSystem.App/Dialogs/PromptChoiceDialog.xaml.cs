using System.Windows;
using MasterBookWritingSystem.App.Services;

namespace MasterBookWritingSystem.App.Dialogs;

public partial class PromptChoiceDialog : Window
{
    public PromptChoiceDialog(string title, string prompt, IReadOnlyList<DialogChoice> choices)
    {
        InitializeComponent();
        Title = title;
        Prompt = prompt;
        Choices = choices;
        DataContext = this;
        if (choices.Count > 0)
        {
            ChoicesList.SelectedIndex = 0;
        }
    }

    public string Prompt { get; }

    public IReadOnlyList<DialogChoice> Choices { get; }

    public DialogChoice? SelectedChoice { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ChoicesList.SelectedItem is not DialogChoice choice)
        {
            return;
        }

        SelectedChoice = choice;
        DialogResult = true;
    }
}
