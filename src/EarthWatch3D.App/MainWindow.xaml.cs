using System.Windows;
using EarthWatch3D.App.Controllers;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Feeds.Nws;

namespace EarthWatch3D.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm, EventStore store, ZoneCache zones)
    {
        InitializeComponent();
        DataContext = vm;
        Scene = new SceneController(Viewport, Overlay, Card, vm, this, store, zones);
    }

    public SceneController Scene { get; }
}
