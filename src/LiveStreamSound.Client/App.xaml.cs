using System.Windows;
using LiveStreamSound.Client.Services;
using Wpf.Ui.Appearance;

namespace LiveStreamSound.Client;

public partial class App : Application
{
    public static ClientOrchestrator Orchestrator { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var system = ApplicationThemeManager.GetSystemTheme();
        var initial = system == SystemTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(initial);
        Orchestrator.Discovery.Start();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        await Orchestrator.DisposeAsync();
    }
}
