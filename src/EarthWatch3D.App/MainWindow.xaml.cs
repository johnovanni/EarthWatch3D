using System.Windows;
using EarthWatch3D.App.Controllers;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm, IClock clock)
    {
        InitializeComponent();
        DataContext = vm;
        Scene = new SceneController(Viewport, Overlay, vm, clock, this);
    }

    public SceneController Scene { get; }
}
