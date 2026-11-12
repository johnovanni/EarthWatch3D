using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EarthWatch3D.App.Rendering;
using EarthWatch3D.App.Services;
using EarthWatch3D.Core;

namespace EarthWatch3D.App.ViewModels;

/// <summary>One line of the About window's lists: a name that links to <paramref name="Url"/>, and what it is credited for.</summary>
public sealed record Credit(string Name, string Text, string Url);

/// <summary>The About window: version, every data credit, the libraries' licences, the data folder.</summary>
public sealed partial class AboutViewModel : ObservableObject
{
    public const string NoticesFileName = "THIRD-PARTY-NOTICES.txt";

    private readonly ILinkOpener _opener;

    public AboutViewModel(ILinkOpener opener, string dataFolder, bool mtgAvailable = LightningLayer.MtgAvailable)
    {
        _opener = opener ?? throw new ArgumentNullException(nameof(opener));
        DataFolder = dataFolder;
        Credits = BuildCredits(mtgAvailable);
    }

    public string Version { get; } = "Version " + AppInfo.Version;

    public IReadOnlyList<Credit> Credits { get; }

    public IReadOnlyList<Credit> Licences { get; } =
    [
        new("HelixToolkit.Wpf", "MIT licence", "http://www.helix-toolkit.org/"),
        new("CommunityToolkit.Mvvm", "MIT licence", "https://github.com/CommunityToolkit/dotnet"),
        new("PureHDF", "MIT licence", "https://apollo3zehn.github.io/PureHDF/"),
        new(".NET", "MIT licence", "https://github.com/dotnet/runtime"),
    ];

    public string DataFolder { get; }

    /// <summary>Why the last link, folder or file could not be opened; null otherwise.</summary>
    [ObservableProperty] private string? _message;

    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (!string.IsNullOrEmpty(url)) TryOpen(url);
    }

    [RelayCommand]
    private void OpenDataFolder() => TryOpen(DataFolder);

    [RelayCommand]
    private void OpenNotices() => TryOpen(Path.Combine(AppContext.BaseDirectory, NoticesFileName));

    // The target stays in the message so the user can copy it into a browser.
    private void TryOpen(string target)
    {
        try
        {
            _opener.Open(target);
            Message = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Message = $"Could not open {target}: {ex.Message}";
        }
    }

    private static IReadOnlyList<Credit> BuildCredits(bool mtgAvailable)
    {
        var list = new List<Credit>
        {
            new("USGS", "Earthquakes: U.S. Geological Survey real-time feeds", "https://earthquake.usgs.gov/earthquakes/feed/"),
            new("NOAA National Hurricane Center", "Tropical cyclones (Atlantic and East/Central Pacific)", "https://www.nhc.noaa.gov/"),
            new("GDACS", "Tropical cyclones worldwide: Global Disaster Alert and Coordination System (European Commission and United Nations)", "https://www.gdacs.org/"),
            new("NOAA National Weather Service", "US weather alerts and forecast zones", "https://www.weather.gov/"),
            new("NASA EONET", "Severe storms: Earth Observatory Natural Event Tracker", "https://eonet.gsfc.nasa.gov/"),
            new("NOAA GOES GLM", "Lightning: GOES-19 and GOES-18 Geostationary Lightning Mapper, NOAA", "https://registry.opendata.aws/noaa-goes/"),
        };
        if (mtgAvailable)
            list.Add(new("EUMETSAT", "Lightning over Europe and Africa: Contains EUMETSAT data", "https://www.eumetsat.int/"));
        list.AddRange(
        [
            new("NASA GIBS", "Clouds: VIIRS NOAA-21 true colour, NASA Global Imagery Browse Services", "https://www.earthdata.nasa.gov/engage/open-data-services-software/earthdata-developer-portal/gibs-api"),
            new("NASA Blue Marble", "Globe imagery: NASA Earth Observatory, Blue Marble Next Generation (R. Stöckli, 2004)", "https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/"),
            new("Natural Earth", "Coastlines and borders (public domain)", "https://www.naturalearthdata.com/"),
            new("Plate boundaries", "Peter Bird (2003), PB2002; GIS conversion by Hugo Ahlenius, Nordpil. ODC-By 1.0", "https://github.com/fraxen/tectonicplates"),
        ]);
        return list;
    }
}
