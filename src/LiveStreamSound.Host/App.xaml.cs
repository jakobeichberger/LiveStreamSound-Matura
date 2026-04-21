using System.Windows;
using LiveStreamSound.Host.Services;
using Wpf.Ui.Appearance;

namespace LiveStreamSound.Host;

public partial class App : Application
{
    public static HostOrchestrator Orchestrator { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var system = ApplicationThemeManager.GetSystemTheme();
        var initial = system == SystemTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(initial);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        await Orchestrator.DisposeAsync();
    }
}
