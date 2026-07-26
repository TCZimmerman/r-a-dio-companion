using System.Net;
using System.Text.RegularExpressions;

namespace RadioCompanion;

public static partial class MetadataParser
{
    public sealed record CurrentTrack(string Title, string Tags, long StartMs, double DurationSeconds, string DurationText);
    public sealed record Streamer(string Name, string ImagePath);

    public static CurrentTrack? ParseMetadata(string html)
    {
        var title = InnerTextById(html, "metadata");
        if (string.IsNullOrWhiteSpace(title)) return null;

        var tags = InnerTextById(html, "now-playing-tags") ?? string.Empty;
        var startText = AttributeById(html, "progress-current", "data-start");
        var durationText = InnerTextById(html, "progress-max") ?? "00:00";
        var maxText = Regex.Match(html, "<progress[^>]*id=[\\\"']current-song-progress[\\\"'][^>]*max=[\\\"'](?<v>[^\\\"']+)", RegexOptions.IgnoreCase).Groups["v"].Value;

        _ = long.TryParse(startText, out var startMs);
        _ = double.TryParse(maxText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration);
        return new CurrentTrack(title, tags, startMs, duration, durationText);
    }

    public static Streamer? ParseStreamer(string html)
    {
        var name = InnerTextById(html, "dj-name");
        var image = Regex.Match(html, "<img[^>]*src=[\\\"'](?<v>[^\\\"']+)", RegexOptions.IgnoreCase).Groups["v"].Value;
        return string.IsNullOrWhiteSpace(name) ? null : new Streamer(name, WebUtility.HtmlDecode(image));
    }

    public static IReadOnlyList<TrackItem> ParseQueue(string html)
    {
        var results = new List<TrackItem>();
        var itemPattern = new Regex("<li(?<attrs>[^>]*)>(?<body>.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match item in itemPattern.Matches(html))
        {
            var body = item.Groups["body"].Value;
            var titleMatch = Regex.Match(body, "<span[^>]*class=[\\\"'][^\\\"']*queue-meta[^\\\"']*[\\\"'][^>]*>(?<v>.*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!titleMatch.Success) continue;
            var title = Clean(titleMatch.Groups["v"].Value);
            var timeMatch = Regex.Match(body, "<time[^>]*datetime=[\\\"'](?<v>\\d+)", RegexOptions.IgnoreCase);
            long? timestamp = long.TryParse(timeMatch.Groups["v"].Value, out var t) ? t : null;
            var request = body.Contains("is-request", StringComparison.OrdinalIgnoreCase);
            results.Add(new TrackItem(title, timestamp, request));
        }
        return results;
    }

    public static IReadOnlyList<TrackItem> ParseLastPlayed(string html)
    {
        var results = new List<TrackItem>();
        var itemPattern = new Regex("<li(?<attrs>[^>]*)>(?<body>.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match item in itemPattern.Matches(html))
        {
            var body = item.Groups["body"].Value;
            var titleMatch = Regex.Match(body, "<span[^>]*class=[\\\"'][^\\\"']*lp-meta[^\\\"']*[\\\"'][^>]*>(?<v>.*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!titleMatch.Success) continue;
            var title = Clean(titleMatch.Groups["v"].Value);
            var timeMatch = Regex.Match(body, "<time[^>]*datetime=[\\\"'](?<v>\\d+)", RegexOptions.IgnoreCase);
            long? timestamp = long.TryParse(timeMatch.Groups["v"].Value, out var t) ? t : null;
            results.Add(new TrackItem(title, timestamp));
        }
        return results;
    }

    private static string? InnerTextById(string html, string id)
    {
        var pattern = $"<(?<tag>[a-z0-9]+)[^>]*id=[\\\"']{Regex.Escape(id)}[\\\"'][^>]*>(?<v>.*?)</\\k<tag>>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? Clean(match.Groups["v"].Value) : null;
    }

    private static string? AttributeById(string html, string id, string attribute)
    {
        var element = Regex.Match(html, $"<[^>]*id=[\\\"']{Regex.Escape(id)}[\\\"'][^>]*>", RegexOptions.IgnoreCase);
        if (!element.Success) return null;
        var attr = Regex.Match(element.Value, $"{Regex.Escape(attribute)}=[\\\"'](?<v>[^\\\"']*)", RegexOptions.IgnoreCase);
        return attr.Success ? WebUtility.HtmlDecode(attr.Groups["v"].Value) : null;
    }

    private static string Clean(string value)
    {
        var withoutTags = Regex.Replace(value, "<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }
}
