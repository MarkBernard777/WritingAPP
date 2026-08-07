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

public sealed class PublishingViewModel()
    : PlaceholderViewModel("Publishing", "Publishing route, launch and post-publication modules will appear here.");

public sealed class SettingsViewModel()
    : PlaceholderViewModel("Settings", "Application preferences will appear here.");
