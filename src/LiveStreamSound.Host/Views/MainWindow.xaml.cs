using LiveStreamSound.Host.ViewModels;

namespace LiveStreamSound.Host.Views;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(App.Orchestrator);
    }
}
