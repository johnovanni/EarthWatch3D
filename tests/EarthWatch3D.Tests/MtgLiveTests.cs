using System.IO;
using System.Text.Json;
using EarthWatch3D.Core.Feeds;
using EarthWatch3D.Core.Lightning.Mtg;
using EarthWatch3D.Core.Time;
using Xunit.Abstractions;

namespace EarthWatch3D.Tests;

/// <summary>
/// Talks to the real EUMETSAT service with the developer's own key from the git-ignored tools/eumetsat.local.json
/// ({"consumerKey", "consumerSecret"}). Returns at once when that file is absent. Never writes the key, the secret or the
/// token anywhere.
/// </summary>
public class MtgLiveTests(ITestOutputHelper output)
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EarthWatch3D.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("EarthWatch3D.sln not found above the test assembly.");
    }

    private sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }

    [Fact]
    [Trait("Category", "Network")]
    public async Task RealServiceAnswers()
    {
        var path = Path.Combine(RepoRoot(), "tools", "eumetsat.local.json");
        if (!File.Exists(path)) { output.WriteLine("tools/eumetsat.local.json is absent: nothing checked."); return; }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var creds = new EumetsatCredentials(
            doc.RootElement.GetProperty("consumerKey").GetString()!, doc.RootElement.GetProperty("consumerSecret").GetString()!);

        using var http = new FeedHttp();
        var client = new EumetsatClient(http, new SystemClock());
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var products = await client.SearchAsync(creds, DateTime.UtcNow.AddMinutes(-40), cts.Token);
        Assert.NotEmpty(products);
        var newest = products[0];   // newest first
        output.WriteLine($"{products.Count} products; newest {newest.SensingStartUtc:HH:mm:ss}-{newest.SensingEndUtc:HH:mm:ss} UTC");

        var bytes = await client.DownloadAsync(creds, newest, cts.Token);
        output.WriteLine($"downloaded {bytes.Length:N0} bytes");
        var flashes = MtgReader.Read(bytes);
        output.WriteLine($"{flashes.Count:N0} flashes read (kept after the confidence filter)");
        Assert.All(flashes, f => Assert.Equal(EarthWatch3D.Core.Lightning.LightningSensor.Mtg, f.Sensor));
    }
}
