namespace MasterBookWritingSystem.App.Services;

public sealed class CreateProjectDialogResult
{
    public required string Title { get; init; }

    public string Author { get; init; } = string.Empty;

    public string Genre { get; init; } = string.Empty;

    public string NorthStar { get; init; } = string.Empty;
}

public sealed class DialogChoice
{
    public required Guid Id { get; init; }

    public required string Label { get; init; }
}

public interface IProjectDialogService
{
    string? PickFolder(string description);

    CreateProjectDialogResult? PromptCreateProject();

    void ShowMessage(string message, string caption);

    bool Confirm(string message, string caption);

    string? PromptText(string title, string prompt, string? initialValue = null);

    DialogChoice? PromptChoice(string title, string prompt, IReadOnlyList<DialogChoice> choices);

    string? PickOpenFile(string title, string filter);

    string? PickSaveFile(string title, string filter, string defaultFileName);
}
