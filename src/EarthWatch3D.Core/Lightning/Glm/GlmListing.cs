using System.Globalization;
using System.Xml;

namespace EarthWatch3D.Core.Lightning.Glm;

/// <summary>One GLM file in a NOAA S3 bucket listing. The 20 s coverage comes from the key's s and e fields.</summary>
public sealed record GlmObject(string Key, DateTime StartUtc, DateTime EndUtc, long Size);

/// <summary>Builds and parses NOAA GOES S3 (ListObjectsV2) listings of GLM-L2-LCFA files.</summary>
public static class GlmListing
{
    /// <summary>The key prefix of the hour containing <paramref name="hourUtc"/>: <c>GLM-L2-LCFA/yyyy/ddd/HH/</c> (ddd = day of year).</summary>
    public static string Prefix(DateTime hourUtc) =>
        string.Create(CultureInfo.InvariantCulture, $"GLM-L2-LCFA/{hourUtc.Year:0000}/{hourUtc.DayOfYear:000}/{hourUtc.Hour:00}/");

    public static Uri ListUri(string bucket, string prefix, string? startAfter)
    {
        var url = $"https://{bucket}.s3.amazonaws.com/?list-type=2&prefix={Uri.EscapeDataString(prefix)}";
        if (!string.IsNullOrEmpty(startAfter)) url += $"&start-after={Uri.EscapeDataString(startAfter)}";
        return new Uri(url);
    }

    /// <summary>Parses an S3 ListObjectsV2 response. Objects whose key is not a GLM file name are skipped.</summary>
    public static IReadOnlyList<GlmObject> Parse(Stream xml)
    {
        var result = new List<GlmObject>();
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(xml, settings);
            while (reader.ReadToFollowing("Contents"))
            {
                string? key = null;
                long size = 0;
                using var sub = reader.ReadSubtree();
                sub.Read(); // the Contents element itself
                sub.Read(); // its first child
                while (!sub.EOF)
                {
                    if (sub.NodeType != XmlNodeType.Element) { sub.Read(); continue; }
                    if (sub.LocalName == "Key") key = sub.ReadElementContentAsString();
                    else if (sub.LocalName == "Size") size = sub.ReadElementContentAsLong();
                    else sub.Skip();
                }
                if (key is not null && TryParseKey(key, out var s, out var e)) result.Add(new GlmObject(key, s, e, size));
            }
        }
        catch (XmlException ex) { throw new FormatException("Not a valid S3 listing: " + ex.Message, ex); }
        return result;
    }

    /// <summary>Reads the s (start) and e (end) fields, <c>yyyydddHHMMSSt</c> (t = tenths of a second), of a GLM file name.</summary>
    public static bool TryParseKey(string key, out DateTime startUtc, out DateTime endUtc)
    {
        startUtc = endUtc = default;
        var name = key[(key.LastIndexOf('/') + 1)..];
        if (!name.EndsWith(".nc", StringComparison.Ordinal)) return false;
        var parts = name[..^3].Split('_');
        string? s = null, e = null;
        foreach (var p in parts)
        {
            if (p.Length == 15 && p[0] == 's') s = p[1..];
            else if (p.Length == 15 && p[0] == 'e') e = p[1..];
        }
        return s is not null && e is not null && TryParseStamp(s, out startUtc) && TryParseStamp(e, out endUtc);
    }

    private static bool TryParseStamp(string t, out DateTime utc)
    {
        utc = default;
        foreach (var c in t) if (c < '0' || c > '9') return false;
        int Num(int from, int len) => int.Parse(t.AsSpan(from, len), CultureInfo.InvariantCulture);
        int year = Num(0, 4), doy = Num(4, 3), hh = Num(7, 2), mm = Num(9, 2), ss = Num(11, 2), tenth = Num(13, 1);
        var days = DateTime.IsLeapYear(year) ? 366 : 365;
        if (year < 1 || doy < 1 || doy > days || hh > 23 || mm > 59 || ss > 60) return false;
        utc = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(doy - 1).AddHours(hh).AddMinutes(mm).AddSeconds(ss).AddMilliseconds(tenth * 100);
        return true;
    }
}
