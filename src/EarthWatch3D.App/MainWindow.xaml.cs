using System.Windows;
using EarthWatch3D.App.Controllers;
using EarthWatch3D.App.ViewModels;

namespace EarthWatch3D.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Scene = new SceneController(Viewport, Overlay, Card, vm, this);
    }

    public SceneController Scene { get; }
}
