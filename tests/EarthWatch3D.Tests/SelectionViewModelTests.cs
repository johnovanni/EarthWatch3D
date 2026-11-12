using AstroCore.Earth;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core.Events;
using EarthWatch3D.Core.Geo;

namespace EarthWatch3D.Tests;

public class SelectionViewModelTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T = new(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc);
    private static SelectionViewModel Vm() => new(() => Now);

    private static Earthquake Quake(string id = "q1") =>
        new(id, T, new GeoPosition(10, 20), "M 6.1 - Somewhere", new Uri("https://example.org/q"), 6.1, 35, "10 km N of Somewhere", T);

    private static CycloneAdvisory Storm(double? kt, string id = "nhc:1") =>
        new(EventSource.Nhc, id, T, new GeoPosition(15, -50), "t", null, "PENA", kt, 1002, [], [], [], []);

    private static WeatherAlert Alert(string id = "a1", string desc = "Take cover.") =>
        new(id, T, new GeoPosition(0, 0), "Tornado Warning issued for Foo County", null, "Tornado Warning", "Extreme",
            T, new DateTime(2026, 11, 12, 15, 30, 0, DateTimeKind.Utc), null, [], [], desc);

    [Fact]
    public void QuakeLines()
    {
        var vm = Vm();
        vm.Select(Quake());
        Assert.True(vm.IsOpen);
        Assert.Equal(new Uri("https://example.org/q"), vm.Link);
        Assert.Equal(
            [("Magnitude", "M6.1"), ("Depth", "35 km"), ("Time", "2026-11-12 06:00 UTC (2 h ago)"), ("Place", "10 km N of Somewhere")],
            vm.Lines);
    }

    [Theory]
    [InlineData(null, "Tropical depression")]
    [InlineData(20.0, "Tropical depression")]
    [InlineData(40.0, "Tropical storm")]
    [InlineData(70.0, "Category 1 hurricane")]
    [InlineData(90.0, "Category 2 hurricane")]
    [InlineData(150.0, "Category 5 hurricane")]
    public void CycloneCategoryText(double? kt, string expected)
    {
        var vm = Vm();
        vm.Select(Storm(kt));
        Assert.Equal(expected, vm.Lines.First(l => l.Label == "Category").Value);
    }

    [Fact]
    public void CycloneLines()
    {
        var vm = Vm();
        vm.Select(Storm(40));
        Assert.Equal("PENA", vm.Title);
        Assert.Contains(("Wind", "40 kt (74 km/h)"), vm.Lines);
        Assert.Contains(("Pressure", "1002 hPa"), vm.Lines);
        Assert.Contains(("Source", "NHC"), vm.Lines);
    }

    [Fact]
    public void AlertLines()
    {
        var vm = Vm();
        vm.Select(Alert());
        Assert.Equal("Tornado Warning", vm.Title);
        Assert.Contains(("Event", "Tornado Warning"), vm.Lines);
        Assert.Contains(("Severity", "Extreme"), vm.Lines);
        Assert.Contains(("From", "2026-11-12 06:00 UTC"), vm.Lines);
        Assert.Contains(("Until", "2026-11-12 15:30 UTC"), vm.Lines);
        Assert.Equal("Take cover.", vm.Description);
    }

    [Fact]
    public void LongDescriptionIsCut()
    {
        var vm = Vm();
        vm.Select(Alert(desc: new string('x', 2000)));
        Assert.Equal(SelectionViewModel.MaxDescriptionChars + 1, vm.Description.Length);
    }

    [Fact]
    public void EonetLines()
    {
        var vm = Vm();
        var s = new EonetStorm("e1", T, new GeoPosition(0, 0), "Storm X", null,
            [new StormFix(T.AddHours(-3), new GeoPosition(0, 0), 30), new StormFix(T, new GeoPosition(1, 1), 50)], null);
        vm.Select(s);
        Assert.Contains(("Wind", "50 kt (93 km/h)"), vm.Lines);
        Assert.Contains(("Source", "NASA EONET"), vm.Lines);
        Assert.Equal("Storm X", vm.Title);
    }

    [Fact]
    public void HoverTexts()
    {
        Assert.Equal("M6.1 · 35 km deep · 2 h ago", SelectionViewModel.HoverText(Quake(), Now));
        Assert.Equal("PENA · Tropical storm · 40 kt", SelectionViewModel.HoverText(Storm(40), Now));
        Assert.Equal("Tornado Warning · until 15:30 UTC", SelectionViewModel.HoverText(Alert(), Now));
    }

    [Fact]
    public void SelectionClearsWhenEventLeavesView()
    {
        var vm = Vm();
        var q = Quake();
        vm.Select(q);
        vm.ClearIfNotVisible(new VisibleEvents([new VisibleQuake(q, 0)], [], [], []));
        Assert.True(vm.IsOpen);
        vm.ClearIfNotVisible(new VisibleEvents([new VisibleQuake(Quake("other"), 0)], [], [], []));
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Selected);
    }

    [Fact]
    public void AgesRefreshWhenDisplayTimeMoves()
    {
        var now = Now;
        var vm = new SelectionViewModel(() => now);
        var q = Quake();
        vm.Select(q);
        Assert.Contains("(2 h ago)", vm.Lines.First(l => l.Label == "Time").Value);
        now = Now.AddHours(3);
        vm.ClearIfNotVisible(new VisibleEvents([new VisibleQuake(q, 0)], [], [], []));
        Assert.Contains("(5 h ago)", vm.Lines.First(l => l.Label == "Time").Value);
    }

    [Fact]
    public void UnnamedCycloneHoverHasNoLeadingSeparator()
    {
        var c = Storm(40) with { Name = "", Title = "Tropical Cyclone 07" };
        Assert.Equal("Tropical Cyclone 07 · Tropical storm · 40 kt", SelectionViewModel.HoverText(c, Now));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ftp://example.org/x")]
    public void OpenInBrowserRejectsNonHttpSchemes(string url) =>
        Assert.False(EarthWatch3D.App.Views.DetailsPanel.OpenInBrowser(new Uri(url)));

    [Fact]
    public void CloseCommandClosesAndUpdatedRecordRefreshes()
    {
        var vm = Vm();
        vm.Select(Storm(40));
        vm.ClearIfNotVisible(new VisibleEvents([], [Storm(70)], [], []));   // same id, newer advisory
        Assert.Equal("Category 1 hurricane", vm.Lines.First(l => l.Label == "Category").Value);
        vm.CloseCommand.Execute(null);
        Assert.False(vm.IsOpen);
    }
}
