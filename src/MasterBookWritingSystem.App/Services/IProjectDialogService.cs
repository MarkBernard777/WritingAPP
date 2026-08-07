namespace MasterBookWritingSystem.App.Services;

public sealed class CreateProjectDialogResult
{
    public required string Title { get; init; }

    public string Author { get; init; } = string.Empty;

    public string Genre { get; init; } = string.Empty;

    public string NorthStar { get; init; } = string.Empty;
}

public interface IProjectDialogService
{
    string? PickFolder(string description);

    CreateProjectDialogResult? PromptCreateProject();

    void ShowMessage(string message, string caption);
}
