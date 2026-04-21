using System.IO;
using System.Windows;
using System.Windows.Threading;
using LiveStreamSound.App.Services;
using LiveStreamSound.Shared.Diagnostics;
using Wpf.Ui.Appearance;

namespace LiveStreamSound.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize the Windows Event Log sink early so even startup errors
        // end up in eventvwr.msc under the "LiveStreamSound" custom log.
        EventLogSink.Init();

        // Global safety net: the rolling file log used to only capture
        // explicitly-caught network errors. Without these handlers, a WPF
        // binding exception or a stray Task would crash silently. Now every
        // unhandled exception goes through LogCrash → file log + Event Log.
        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash("DispatcherUnhandled", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            LogCrash("AppDomainUnhandled", ex.ExceptionObject as Exception);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            LogCrash("UnobservedTask", ex.Exception);
            ex.SetObserved();
        };

        var system = ApplicationThemeManager.GetSystemTheme();
        var initial = system == SystemTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(initial);
        AppShell.Current.ShowRoleSelection();
    }

    /// <summary>
    /// Logs an unhandled exception to (a) the currently-active orchestrator's
    /// LogService if one exists — so it shows up in-app live — and (b) a crash
    /// dump file under %LOCALAPPDATA%\LiveStreamSound\crashes\ as a last-ditch
    /// record, plus (c) Windows Event Log via the shared sink.
    /// </summary>
    private static void LogCrash(string source, Exception? ex)
    {
        var message = ex?.ToString() ?? "Unknown error (no exception object)";
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [CRASH   ] {source}: {message}";

        // Active orchestrator's in-app log (user can see it immediately)
        try
        {
            AppShell.Current.Host?.Log.Error(source, message, ex);
            AppShell.Current.Client?.Log.Error(source, message, ex);
        }
        catch { /* if the logger itself died, keep going */ }

        // Event log
        try { EventLogSink.Write(LogLevel.Critical, source, message, ex); } catch { }

        // Last-resort crash dump — we can't trust any orchestrator-level log
        // if the whole app is on fire.
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiveStreamSound", "crashes");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"crash-{DateTimeOffset.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path, line + Environment.NewLine + Environment.NewLine);
        }
        catch { }
    }
}
