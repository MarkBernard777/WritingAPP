using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MasterBookWritingSystem.App.Navigation;

public interface INavigationService : INotifyPropertyChanged
{
    ObservableObject CurrentViewModel { get; }

    AppSection CurrentSection { get; }

    void NavigateTo(AppSection section);
}
