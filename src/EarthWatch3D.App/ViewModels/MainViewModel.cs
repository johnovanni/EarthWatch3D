using CommunityToolkit.Mvvm.ComponentModel;
using EarthWatch3D.Core.Time;

namespace EarthWatch3D.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel(IClock clock) => SunMoon = new SunMoonViewModel(clock);

    public SunMoonViewModel SunMoon { get; }

    public void Tick() => SunMoon.Refresh();
}
