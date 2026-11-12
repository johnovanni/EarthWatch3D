using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EarthWatch3D.App.ViewModels;

namespace EarthWatch3D.App.Views;

public partial class TimeBar : UserControl
{
    public TimeBar()
    {
        InitializeComponent();
        TimeSlider.Maximum = TimelineViewModel.Range.TotalSeconds;
    }

    private void OnStepMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }
}
