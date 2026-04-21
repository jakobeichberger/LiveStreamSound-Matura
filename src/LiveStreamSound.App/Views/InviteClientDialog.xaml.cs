using System.Windows;
using LiveStreamSound.App.ViewModels;
using LiveStreamSound.Host.Services;

namespace LiveStreamSound.App.Views;

public partial class InviteClientDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly InviteClientViewModel _vm;

    public InviteClientDialog(HostOrchestrator orchestrator)
    {
        InitializeComponent();
        _vm = new InviteClientViewModel(orchestrator);
        DataContext = _vm;
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        _vm.Dispose();
        Close();
    }
}
