using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveStreamSound.App.Services;
using LiveStreamSound.Shared.Localization;
using Wpf.Ui.Appearance;

namespace LiveStreamSound.App.ViewModels;

public partial class RoleSelectionViewModel : ObservableObject
{
    public Loc Localization => Loc.Instance;
    [ObservableProperty] private bool _isDarkTheme = true;

    public RoleSelectionViewModel()
    {
        IsDarkTheme = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
    }

    [RelayCommand]
    private void ChooseHost() => AppShell.Current.EnterHostMode();

    [RelayCommand]
    private void ChooseClient() => AppShell.Current.EnterClientMode();

    [RelayCommand] private void ToggleLanguage() => Loc.Instance.Toggle();

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplicationThemeManager.Apply(IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }
}
