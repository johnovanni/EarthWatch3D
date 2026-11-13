using System.IO;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Feeds.Gibs;
using EarthWatch3D.Core.Lightning.Glm;
using EarthWatch3D.Core.Lightning.Mtg;
using EarthWatch3D.Core.Logging;

namespace EarthWatch3D.App.Diagnostics;

/// <summary>
/// The <c>--offline-samples &lt;dir&gt;</c> diagnostic: every feed request is answered from the sample files in
/// <c>tests\Samples</c> (captured 2026-11-11, about 19:15–20:30 UTC), so the app can be checked without a network.
/// Anything not mapped gets a 404, which the feeds treat as a failure.
/// </summary>
public static class OfflineSamples
{
    public const string EmptyFeatureCollection = """{"type":"FeatureCollection","features":[]}""";

    /// <summary>The GDACS event whose geometry the sample file holds (Tropical Cyclone PENA-26).</summary>
    public const int GdacsSampleEventId = 1001348;

    /// <summary>The sample files the handler reads, all of which must be in the folder.</summary>
    public static IReadOnlyList<string> Files { get; } =
    [
        "usgs_all_week.geojson", "usgs_all_hour.geojson", "gdacs_events_tc.json", "gdacs_geometry.json",
        "nhc_CurrentStorms.json", "nhc_track.kmz", "nhc_cone.kmz", "nws_active.json", "nws_zone.json", "eonet_storms.json",
        "gibs_viirs_noaa21_truecolor_4096.jpg", "glm_g19_listing.xml", "glm_g19_sample.nc", "glm_g18_sample.nc",
        "eumetsat_token.json", "mtg_search.json", "mtg_lfl_sample.zip",
    ];

    /// <summary>
    /// What the MTG feed signs in with in an offline run: dummy values, never the user's stored key. The sample handler
    /// answers the token request whatever it carries.
    /// </summary>
    public static EumetsatCredentials OfflineCredentials { get; } = new("offline", "offline");

    /// <summary>True when the clouds layer will be on: saved on, or named by <c>--layers</c> (which is applied after the feeds are set up).</summary>
    public static bool CloudsWanted(bool savedOn, IReadOnlyList<string>? layers) =>
        savedOn || layers?.Contains("clouds", StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>True when the lightning layer will be on: saved on, or named by <c>--layers</c> (applied after the feeds are set up).</summary>
    public static bool LightningWanted(bool savedOn, IReadOnlyList<string>? layers) =>
        savedOn || layers?.Contains("lightning", StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>The prefix of the temporary data folders under %TEMP% (one per offline run, deleted on exit).</summary>
    public const string TempFolderPrefix = "EarthWatch3D-offline-";

    /// <summary>
    /// Deletes offline data folders in <paramref name="tempDir"/> last written more than a day before
    /// <paramref name="nowUtc"/>: left behind when a run was killed. Younger ones may belong to a run still open.
    /// Returns how many were deleted; failures are logged, never thrown.
    /// </summary>
    public static int SweepStaleFolders(string tempDir, DateTime nowUtc, IAppLog log)
    {
        int deleted = 0;
        try
        {
            if (!Directory.Exists(tempDir)) return 0;
            foreach (var dir in Directory.EnumerateDirectories(tempDir, TempFolderPrefix + "*"))
            {
                try
                {
                    if (nowUtc - Directory.GetLastWriteTimeUtc(dir) <= TimeSpan.FromDays(1)) continue;
                    Directory.Delete(dir, recursive: true);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log.Warn($"Could not delete the old offline data folder {dir}", ex);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn("Could not look for old offline data folders", ex);
        }
        if (deleted > 0) log.Info($"Deleted {deleted} old offline data folder(s)");
        return deleted;
    }

    /// <summary>
    /// A handler mapping each feed's URL prefixes to the sample files in <paramref name="dir"/> (read on each request).
    /// The URLs are the ones the feed classes use and the ones inside the samples; OfflineSamplesTests runs every feed
    /// through this handler, so a URL that drifts fails a test.
    /// </summary>
    public static SampleHttpHandler CreateHandler(string dir)
    {
        var h = new SampleHttpHandler();
        void Map(string prefix, string file) => h.Map(prefix, Path.Combine(dir, file));

        Map("https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_week.geojson", "usgs_all_week.geojson");
        Map("https://earthquake.usgs.gov/earthquakes/feed/v1.0/summary/all_hour.geojson", "usgs_all_hour.geojson");

        Map("https://www.gdacs.org/gdacsapi/api/events/geteventlist/", "gdacs_events_tc.json");
        // The sample geometry is Pena's. Serving it for the other storms in the event list would draw Pena's track and
        // centre under their names, so they get an empty geometry instead: a glyph at the event list's position, no track.
        h.MapText("https://www.gdacs.org/gdacsapi/api/polygons/getgeometry", EmptyFeatureCollection);
        Map($"https://www.gdacs.org/gdacsapi/api/polygons/getgeometry?eventtype=TC&eventid={GdacsSampleEventId}&", "gdacs_geometry.json");

        // The KMZ links are the ones in nhc_CurrentStorms.json (Pena, CP042026, advisory 020).
        Map("https://www.nhc.noaa.gov/CurrentStorms.json", "nhc_CurrentStorms.json");
        Map("https://www.nhc.noaa.gov/storm_graphics/api/CP042026_020adv_TRACK.kmz", "nhc_track.kmz");
        Map("https://www.nhc.noaa.gov/storm_graphics/api/CP042026_020adv_CONE.kmz", "nhc_cone.kmz");

        Map("https://api.weather.gov/alerts/active", "nws_active.json");
        h.MapText("https://api.weather.gov/alerts?start=", EmptyFeatureCollection);   // the 24 h history: none
        // Every zone gets the one sample zone (PZZ150, off the Washington coast), so every zone-only warning is drawn
        // there. Good enough to see that zones load and draw; not where the warnings really were.
        Map("https://api.weather.gov/zones/", "nws_zone.json");

        Map("https://eonet.gsfc.nasa.gov/api/v3/events", "eonet_storms.json");

        // Any GetMap gets the sample image (TIME=2026-11-10), whatever date was asked for.
        Map(GibsCloudsFeed.UrlPrefix, "gibs_viirs_noaa21_truecolor_4096.jpg");

        // GLM: both buckets list the one sample hour (2026-11-11 19 h UTC, G19 file names) and any other hour is empty.
        // Files are the G19 sample for the East bucket and the G18 sample for the West one, whatever their key.
        const string EmptyListing = "<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"></ListBucketResult>";
        var sampleHour = GlmListing.Prefix(new DateTime(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc));
        foreach (var (bucket, sample) in new[] { ("noaa-goes19", "glm_g19_sample.nc"), ("noaa-goes18", "glm_g18_sample.nc") })
        {
            h.MapText($"https://{bucket}.s3.amazonaws.com/?list-type=2", EmptyListing);
            Map(GlmListing.ListUri(bucket, sampleHour, null).ToString(), "glm_g19_listing.xml");
            Map($"https://{bucket}.s3.amazonaws.com/GLM-L2-LCFA/", sample);
        }

        // MTG: the token, the search (4 products, 2026-11-13 21:10-21:50 UTC) and every product download (the 21:30-21:40
        // product) are the samples whatever the product id.
        Map("https://api.eumetsat.int/token", "eumetsat_token.json");
        Map("https://api.eumetsat.int/data/search-products/1.0.0/os", "mtg_search.json");
        Map("https://api.eumetsat.int/data/download/1.0.0/collections/", "mtg_lfl_sample.zip");
        return h;
    }
}
