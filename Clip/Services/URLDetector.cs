using System.Text.RegularExpressions;
using Clip.Models;

namespace Clip.Services;

public static partial class URLDetector
{
    public static bool TryExtractFirstUrl(string? text, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = UrlRegex().Match(text);
        if (!match.Success)
        {
            return false;
        }

        url = match.Value.Trim().TrimEnd('.', ',', ')', ']');
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static Platform DetectPlatform(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Platform.Unknown;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.Contains("youtube.com") || host.Contains("youtu.be"))
        {
            return Platform.YouTube;
        }

        if (host.Contains("twitter.com") || host == "x.com" || host.EndsWith(".x.com"))
        {
            return Platform.Twitter;
        }

        if (host.Contains("instagram.com"))
        {
            return Platform.Instagram;
        }

        if (host.Contains("tiktok.com"))
        {
            return Platform.TikTok;
        }

        if (host.Contains("reddit.com") || host.Contains("redd.it"))
        {
            return Platform.Reddit;
        }

        return Platform.Unknown;
    }

    public static bool IsSupportedUrl(string? text) => TryExtractFirstUrl(text, out _);

    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();
}
