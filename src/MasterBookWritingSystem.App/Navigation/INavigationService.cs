using CommunityToolkit.Mvvm.ComponentModel;

namespace MasterBookWritingSystem.App.Navigation;

public interface INavigationService : System.ComponentModel.INotifyPropertyChanged
{
    ObservableObject CurrentViewModel { get; }

    AppSection CurrentSection { get; }

    void NavigateTo(AppSection section);

    /// <summary>Opens Story Data on the Scenes tab and selects the given scene.</summary>
    void NavigateToStoryDataScene(Guid sceneId);

    /// <summary>Opens Story Data on the Characters tab and selects the character.</summary>
    void NavigateToStoryDataCharacter(Guid characterId);

    /// <summary>Opens Story Data on the World tab and selects the entry.</summary>
    void NavigateToStoryDataWorldEntry(Guid worldEntryId);

    /// <summary>Opens Story Data on the Beats tab and selects the beat.</summary>
    void NavigateToStoryDataBeat(Guid beatId);

    /// <summary>Opens Manuscript, loads the chapter, and optionally focuses a linked scene.</summary>
    void NavigateToManuscriptChapter(Guid chapterId, Guid? sceneId = null);

    /// <summary>Opens Manuscript in corkboard mode, optionally selecting a scene card.</summary>
    void NavigateToManuscriptCorkboard(Guid? sceneId = null);

    /// <summary>Opens Recovery Centre, optionally preselecting a project root.</summary>
    void NavigateToRecovery(string? projectRootPath = null);
}
