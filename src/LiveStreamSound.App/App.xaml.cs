using System.Windows;
using LiveStreamSound.App.Services;
using Wpf.Ui.Appearance;

namespace LiveStreamSound.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var system = ApplicationThemeManager.GetSystemTheme();
        var initial = system == SystemTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(initial);
        AppShell.Current.ShowRoleSelection();
    }
}
