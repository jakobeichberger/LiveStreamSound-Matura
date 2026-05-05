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
    private void ChooseHost()
    {
        // Emergency-log entry proves the click reached the VM. If this line
        // doesn't appear in the log file but the user reports clicking,
        // the binding from the CardAction button to ChooseHostCommand is
        // broken (likely a XAML resource error in RoleSelectionWindow).
        EmergencyLog.Write("RoleSelectionViewModel.ChooseHost — command received from button");
        AppShell.Current.EnterHostMode();
    }

    [RelayCommand]
    private void ChooseClient()
    {
        EmergencyLog.Write("RoleSelectionViewModel.ChooseClient — command received from button");
        AppShell.Current.EnterClientMode();
    }

    [RelayCommand] private void ToggleLanguage() => Loc.Instance.Toggle();

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplicationThemeManager.Apply(IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }
}
