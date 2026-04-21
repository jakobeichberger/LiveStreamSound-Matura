using System.Windows;
using LiveStreamSound.App.ViewModels;
using LiveStreamSound.Shared.Protocol;

namespace LiveStreamSound.App.Views;

public partial class IncomingInviteDialog : Wpf.Ui.Controls.FluentWindow
{
    public IncomingInviteDialog(Invitation invitation)
    {
        InitializeComponent();
        DataContext = new IncomingInviteViewModel(invitation);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnReject(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
