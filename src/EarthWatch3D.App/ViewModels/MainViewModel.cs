using CommunityToolkit.Mvvm.ComponentModel;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel(IClock clock) => SunMoon = new SunMoonViewModel(clock);

    public SunMoonViewModel SunMoon { get; }

    /// <summary>Raised after each <see cref="Tick"/> has refreshed the view models.</summary>
    public event EventHandler? Ticked;

    public void Tick()
    {
        SunMoon.Refresh();
        Ticked?.Invoke(this, EventArgs.Empty);
    }
}
