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
        Card.SizeChanged += (_, _) => PlaceDetails();
        PlaceDetails();
    }

    public SceneController Scene { get; }

    // The details panel sits just below the Sun & Moon card, which changes height when it collapses.
    private void PlaceDetails() =>
        Details.Margin = new Thickness(12, Card.Margin.Top + Card.ActualHeight + 8, 12, 12);
}
