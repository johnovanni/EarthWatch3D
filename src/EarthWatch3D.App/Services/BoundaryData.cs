using System.IO;
using GlobeCore.Earth;
using GlobeCore.Feeds;
using GlobeCore.Logging;
using GlobeCore.Regions;

namespace EarthWatch3D.App.Services;

/// <summary>
/// The country borders the globe draws (Plan 2b): the newest validated Natural Earth set stored by the "Borders" feed, or
/// the bundled Natural Earth 5.1.2 set, always with the app's rules (<c>data\boundary-rules.json</c>: Crimea shown as part
/// of Ukraine, ruling B-7) applied at load. Downloaded sets must also pass the app's probes (<c>data\boundary-probes.json</c>)
/// before they are used. EarthWatch3D resolves no country names from its feeds, so the validator's names hook is empty.
/// </summary>
public sealed class BoundaryData
{
    public const string RulesFile = "boundary-rules.json";
    public const string ProbesFile = "boundary-probes.json";

    /// <summary>The update feed's name, which its status dot shows (<see cref="BoundaryUpdateFeed.Name"/>).</summary>
    public const string FeedName = "Borders";

    /// <summary>How often the feed asks for a newer release (<see cref="BoundaryUpdateFeed.Interval"/>).</summary>
    public static readonly TimeSpan FeedInterval = TimeSpan.FromHours(24);

    /// <summary>The installed <c>data</c> folder next to the app.</summary>
    public static string InstalledDataDir => Path.Combine(AppContext.BaseDirectory, "data");

    private BoundaryData(BoundaryRules rules, BoundaryValidator validator, BoundaryStore store, CurrentBoundaries current)
    {
        Rules = rules;
        Validator = validator;
        Store = store;
        Current = current;
    }

    public BoundaryRules Rules { get; }
    public BoundaryValidator Validator { get; }
    public BoundaryStore Store { get; }

    /// <summary>The live set; the feed swaps a newer one in (raising <see cref="CurrentBoundaries.Changed"/> on its thread).</summary>
    public CurrentBoundaries Current { get; }

    /// <summary>
    /// Reads the rules and probes from <paramref name="dataDir"/> and loads the set to start with: the newest stored set in
    /// <paramref name="cacheDir"/> newer than the bundled one that is whole, takes the rules and passes validation, else
    /// the bundled set. Slow (the countries file is 3 MB): call it off the UI thread.
    /// </summary>
    /// <exception cref="InvalidDataException">The rules or probes file is malformed.</exception>
    /// <exception cref="BoundaryRuleException">A rule does not match the bundled files.</exception>
    public static BoundaryData Load(string dataDir, string cacheDir, IAppLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        BoundaryRules rules;
        using (var s = File.OpenRead(Path.Combine(dataDir, RulesFile))) rules = BoundaryRules.Parse(s);
        IReadOnlyList<(AstroCore.Earth.GeoPosition Point, string ExpectedId)> probes;
        using (var s = File.OpenRead(Path.Combine(dataDir, ProbesFile))) probes = BoundaryValidator.ReadProbes(s);

        // As NaturalEarthBoundaries.Load, but the bundled set is read once: it also gives the validator its region count.
        var bundled = NaturalEarthBoundaries.LoadBundled(rules);
        var validator = new BoundaryValidator(probes, _ => [], bundled.Regions.All.Count);
        var store = new BoundaryStore(cacheDir);
        var set = bundled;
        if (store.TryLoadNewest(NaturalEarthBoundaries.Parse, rules, validator, out var stored, out var reason,
                newerThanTag: NaturalEarthBoundaries.BundledTag))
        {
            set = stored;
            log.Info($"Boundaries: {stored.Edition} from {store.CacheDir}");
        }
        else log.Info($"Boundaries: bundled {bundled.Edition} ({reason})");
        return new BoundaryData(rules, validator, store, new CurrentBoundaries(set, log));
    }

    /// <summary>
    /// The daily update check on the app's shared HTTP client (its User-Agent; its byte count goes to the download meter),
    /// paused while the meter's daily limit is reached.
    /// </summary>
    public BoundaryUpdateFeed CreateFeed(FeedHttp http, DownloadMeter meter, IAppLog log) =>
        new(new NaturalEarthBoundarySource(http), Rules, Validator, Store, Current, log, meter);
}
