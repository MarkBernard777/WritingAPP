using CommunityToolkit.Mvvm.ComponentModel;

namespace MasterBookWritingSystem.App.Navigation;

public interface INavigationService : System.ComponentModel.INotifyPropertyChanged
{
    ObservableObject CurrentViewModel { get; }

    AppSection CurrentSection { get; }

    void NavigateTo(AppSection section);

    /// <summary>Opens Story Data on the Scenes tab and selects the given scene.</summary>
    void NavigateToStoryDataScene(Guid sceneId);
}
