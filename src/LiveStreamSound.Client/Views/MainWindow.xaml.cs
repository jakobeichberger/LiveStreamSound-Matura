using LiveStreamSound.Client.ViewModels;

namespace LiveStreamSound.Client.Views;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(App.Orchestrator);
    }
}
