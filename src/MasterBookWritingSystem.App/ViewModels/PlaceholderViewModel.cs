using CommunityToolkit.Mvvm.ComponentModel;

namespace MasterBookWritingSystem.App.ViewModels;

public abstract partial class PlaceholderViewModel : ObservableObject
{
    protected PlaceholderViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }

    public string Description { get; }
}

public sealed class DashboardViewModel()
    : PlaceholderViewModel("Dashboard", "Project overview, progress and next actions will appear here.");

public sealed class WorkflowViewModel()
    : PlaceholderViewModel("Workflow", "Guided phases, steps and gates will appear here.");

public sealed class DocumentsViewModel()
    : PlaceholderViewModel("Documents", "The 24 structured working documents will appear here.");

public sealed class ManuscriptViewModel()
    : PlaceholderViewModel("Manuscript", "Chapter editor and compilation tools will appear here.");

public sealed class StoryDataViewModel()
    : PlaceholderViewModel("Story Data", "Characters, world, plot, beats and scenes will appear here.");

public sealed class ToolsViewModel()
    : PlaceholderViewModel("Tools", "Mini tools and diagnostic reports will appear here.");

public sealed class PublishingViewModel()
    : PlaceholderViewModel("Publishing", "Publishing route, launch and post-publication modules will appear here.");

public sealed class SettingsViewModel()
    : PlaceholderViewModel("Settings", "Application preferences will appear here.");
