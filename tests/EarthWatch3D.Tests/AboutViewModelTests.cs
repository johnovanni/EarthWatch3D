using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using EarthWatch3D.App.Services;
using EarthWatch3D.App.ViewModels;
using EarthWatch3D.Core;

namespace EarthWatch3D.Tests;

internal sealed class RecordingLinkOpener : ILinkOpener
{
    public List<string> Opened { get; } = [];
    public Exception? Throw { get; init; }
    public void Open(string target)
    {
        if (Throw is not null) throw Throw;
        Opened.Add(target);
    }
}

public class AboutViewModelTests
{
    private static AboutViewModel Make(ILinkOpener? opener = null, bool mtg = false, string folder = @"C:\data") =>
        new(opener ?? new RecordingLinkOpener(), folder, mtg);

    [Fact]
    public void CreditsNameEveryRequiredSource()
    {
        var text = string.Join("\n", Make().Credits.Select(c => c.Name + " " + c.Text));
        foreach (var needle in new[]
                 {
                     "USGS", "National Hurricane Center", "National Weather Service", "GOES", "GDACS", "EONET", "GIBS",
                     "Blue Marble", "Natural Earth", "Peter Bird (2003), PB2002; GIS conversion by Hugo Ahlenius, Nordpil", "ODC-By 1.0",
                 })
            Assert.Contains(needle, text);
    }

    [Fact]
    public void EumetsatCreditOnlyWhenMtgIsAvailable()
    {
        Assert.DoesNotContain(Make(mtg: false).Credits, c => (c.Name + c.Text).Contains("EUMETSAT"));
        var with = Make(mtg: true).Credits.Where(c => (c.Name + c.Text).Contains("EUMETSAT")).ToList();
        Assert.Single(with);
        Assert.Contains("Contains EUMETSAT data", with[0].Text);
    }

    [Fact]
    public void LicencesListTheShippedLibraries()
    {
        var l = Make().Licences;
        foreach (var name in new[] { "HelixToolkit.Wpf", "CommunityToolkit.Mvvm", "PureHDF", "WPF", ".NET" })
            Assert.Equal("MIT licence", Assert.Single(l, c => c.Name == name).Text);
    }

    [Fact]
    public void ShowsTheVersionAndFolder()
    {
        var vm = Make(folder: @"C:\x");
        Assert.Equal("Version " + AppInfo.Version, vm.Version);
        Assert.Equal(@"C:\x", vm.DataFolder);
    }

    [Fact]
    public void OpenLinkPassesTheUrl()
    {
        var opener = new RecordingLinkOpener();
        Make(opener).OpenLinkCommand.Execute("https://example.org/a");
        Assert.Equal(["https://example.org/a"], opener.Opened);
    }

    [Fact]
    public void LinkThatFailsToOpenShowsAMessage()
    {
        var vm = Make(new RecordingLinkOpener { Throw = new Win32Exception("no browser") });
        vm.OpenLinkCommand.Execute("https://example.org/a");
        Assert.NotNull(vm.Message);
        Assert.StartsWith("Could not open", vm.Message);
        Assert.Contains("https://example.org/a", vm.Message);
    }

    [Fact]
    public void DataFolderOpensThroughTheOpener()
    {
        var opener = new RecordingLinkOpener();
        Make(opener, folder: @"C:\data").OpenDataFolderCommand.Execute(null);
        Assert.Equal([@"C:\data"], opener.Opened);
    }

    [Fact]
    public void NoticesOpenThroughTheOpenerAndFailureShowsAMessage()
    {
        var opener = new RecordingLinkOpener();
        Make(opener).OpenNoticesCommand.Execute(null);
        Assert.EndsWith("THIRD-PARTY-NOTICES.txt", Assert.Single(opener.Opened));
        var vm = Make(new RecordingLinkOpener { Throw = new FileNotFoundException("gone") });
        vm.OpenNoticesCommand.Execute(null);
        Assert.StartsWith("Could not open", vm.Message);
    }

    [Fact]
    public void EveryCreditAndLicenceUrlIsHttp()
    {
        var vm = Make(mtg: true);
        foreach (var c in vm.Credits.Concat(vm.Licences))
            Assert.True(Uri.TryCreate(c.Url, UriKind.Absolute, out var u) && (u.Scheme is "http" or "https"), c.Url);
    }

    [Fact, Trait("Category", "Network")]
    public async Task CreditLinksAnswer()
    {
        var vm = Make(mtg: true);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
        var bad = new List<string>();
        foreach (var c in vm.Credits.Concat(vm.Licences))
        {
            var status = await StatusAsync(http, c.Url);
            Console.WriteLine($"{(int)status} {c.Url}");
            if ((int)status is < 200 or >= 400) bad.Add($"{(int)status} {c.Url}");
        }
        Assert.True(bad.Count == 0, "Dead links: " + string.Join("; ", bad));
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient http, string url)
    {
        try
        {
            using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), HttpCompletionOption.ResponseHeadersRead);
            if ((int)head.StatusCode is >= 200 and < 400) return head.StatusCode;
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        try
        {
            using var get = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            return get.StatusCode;
        }
        catch (Exception) { return 0; }
    }
}
