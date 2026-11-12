using System.IO;
using EarthWatch3D.Core.Lightning.Glm;

namespace EarthWatch3D.Tests;

public class GlmListingTests
{
    private static string Sample(string f) => Path.Combine(AppContext.BaseDirectory, "Samples", f);

    [Fact]
    public void ParsesListing()
    {
        using var s = File.OpenRead(Sample("glm_g19_listing.xml"));
        var objs = GlmListing.Parse(s);

        Assert.Equal(180, objs.Count);
        Assert.Equal("GLM-L2-LCFA/2026/315/19/OR_GLM-L2-LCFA_G19_s20263151900000_e20263151900200_c20263151900219.nc", objs[0].Key);
        Assert.Equal(392919, objs[0].Size);
        Assert.Equal(new DateTime(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc), objs[0].StartUtc);
        Assert.Equal(DateTimeKind.Utc, objs[0].EndUtc.Kind);
        Assert.Equal(TimeSpan.FromSeconds(20), objs[0].EndUtc - objs[0].StartUtc);
        Assert.Equal(objs.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key), objs.Select(o => o.Key));
        Assert.Equal(new DateTime(2026, 11, 11, 19, 59, 40, DateTimeKind.Utc), objs[179].StartUtc);
    }

    [Fact]
    public void PrefixRollsOverAtMidnightNewYearAndLeapDay()
    {
        Assert.Equal("GLM-L2-LCFA/2026/315/19/", GlmListing.Prefix(new DateTime(2026, 11, 11, 19, 42, 5, DateTimeKind.Utc)));
        Assert.Equal("GLM-L2-LCFA/2026/365/23/", GlmListing.Prefix(new DateTime(2026, 12, 31, 23, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("GLM-L2-LCFA/2027/001/00/", GlmListing.Prefix(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal("GLM-L2-LCFA/2028/366/05/", GlmListing.Prefix(new DateTime(2028, 12, 31, 5, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void ListUriUsesStartAfter()
    {
        var p = "GLM-L2-LCFA/2026/315/19/";
        var k = "GLM-L2-LCFA/2026/315/19/OR_x s1.nc";
        var uri = GlmListing.ListUri("noaa-goes19", p, k);
        Assert.Equal($"https://noaa-goes19.s3.amazonaws.com/?list-type=2&prefix={Uri.EscapeDataString(p)}&start-after={Uri.EscapeDataString(k)}", uri.OriginalString);
        Assert.Equal($"https://noaa-goes19.s3.amazonaws.com/?list-type=2&prefix={Uri.EscapeDataString(p)}", GlmListing.ListUri("noaa-goes19", p, null).OriginalString);
    }

    [Fact]
    public void TryParseKeyRejectsOtherKeys()
    {
        Assert.False(GlmListing.TryParseKey("GLM-L2-LCFA/2026/315/19/", out _, out _));
        Assert.False(GlmListing.TryParseKey("GLM-L2-LCFA/2026/315/19/OR_GLM-L2-LCFA_G19_sXX_e1_c1.nc", out _, out _));
        Assert.True(GlmListing.TryParseKey("OR_GLM-L2-LCFA_G19_s20263151900123_e20263151900323_c20263151900340.nc", out var s, out var e));
        Assert.Equal(new DateTime(2026, 11, 11, 19, 0, 12, 300, DateTimeKind.Utc), s);
        Assert.Equal(new DateTime(2026, 11, 11, 19, 0, 32, 300, DateTimeKind.Utc), e);
    }
}
