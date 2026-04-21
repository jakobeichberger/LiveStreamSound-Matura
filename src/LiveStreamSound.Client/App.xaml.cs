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
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        Orchestrator.Discovery.Start();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        await Orchestrator.DisposeAsync();
    }
}
