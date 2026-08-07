using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterBookWritingSystem.App.Navigation;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    public ShellViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(INavigationService.CurrentViewModel)
                or nameof(INavigationService.CurrentSection)
                or null)
            {
                OnPropertyChanged(nameof(CurrentViewModel));
                OnPropertyChanged(nameof(CurrentSection));
            }
        };
    }

    public ObservableObject CurrentViewModel => _navigationService.CurrentViewModel;

    public AppSection CurrentSection => _navigationService.CurrentSection;

    public string ApplicationTitle => "Master Book-Writing System";

    [RelayCommand]
    private void Navigate(AppSection section) => _navigationService.NavigateTo(section);
}
