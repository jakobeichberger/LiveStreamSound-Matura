using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveStreamSound.Shared.Localization;
using LiveStreamSound.Shared.Session;

namespace LiveStreamSound.App.ViewModels;

public partial class ClientGroupViewModel : ObservableObject
{
    public LaptopCategory Category { get; }
    public ObservableCollection<ClientTileViewModel> Clients { get; } = new();

    [ObservableProperty] private string _label = "";

    public ClientGroupViewModel(LaptopCategory category)
    {
        Category = category;
        RefreshLabel();
        Loc.Instance.PropertyChanged += (_, _) => RefreshLabel();
    }

    public int Count => Clients.Count;

    private void RefreshLabel()
    {
        var key = Category switch
        {
            LaptopCategory.Klassenraum => "Host.GroupClassrooms",
            LaptopCategory.Werkstatt => "Host.GroupWorkshop",
            LaptopCategory.Raum => "Host.GroupRooms",
            _ => "Host.GroupOther",
        };
        Label = Loc.Instance.Get(key);
    }
}
