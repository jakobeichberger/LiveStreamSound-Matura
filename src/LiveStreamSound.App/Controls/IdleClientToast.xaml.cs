using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace LiveStreamSound.App.Controls;

public partial class IdleClientToast : UserControl
{
    public IdleClientToast()
    {
        InitializeComponent();
        // The breath-pulse storyboard runs forever; stop+remove it on unload
        // so clocks are detached when the toast is removed from the dashboard.
        // Otherwise long sessions where many toasts come and go accumulate
        // animation clocks in WPF's timing tree.
        Unloaded += OnToastUnloaded;
    }

    private void OnToastUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ToastStoryboard.Storyboard.Stop(this);
            ToastStoryboard.Storyboard.Remove(this);
        }
        catch
        {
            // Best-effort cleanup — Stop/Remove can throw if the storyboard
            // never started (e.g. control unloaded before Loaded fired).
        }
    }
}
