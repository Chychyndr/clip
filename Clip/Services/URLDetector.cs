using System.Text.RegularExpressions;
using Clip.Models;

namespace Clip.Services;

public static partial class URLDetector
{
    private static readonly char[] UrlEndDelimiters = [' ', '\t', '\r', '\n', ';', '<', '>', '"'];
    private static readonly char[] UrlTrimStart = ['(', '[', '{', '"', '\''];
    private static readonly char[] UrlTrimEnd = ['.', ',', ';', ':', ')', ']', '}', '"', '\'', '!', '?'];

    public static bool TryExtractFirstUrl(string? text, out string url)
    {
        url = "";
        var urls = ExtractUrls(text);
        if (urls.Count == 0)
        {
            return false;
        }

        url = urls[0];
        return true;
    }

    public static bool TryExtractFirstSupportedUrl(string? text, out string url)
    {
        url = "";
        var urls = ExtractSupportedUrls(text);
        if (urls.Count == 0)
        {
            return false;
        }

        url = urls[0];
        return true;
    }

    public static IReadOnlyList<string> ExtractUrls(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var matches = UrlStartRegex().Matches(text);
        if (matches.Count == 0)
        {
            return [];
        }

        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : text.Length;
            var candidate = CleanUrlCandidate(text[start..end]);
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !seen.Add(candidate))
            {
                continue;
            }

            urls.Add(candidate);
        }

        return urls;
    }

    public static IReadOnlyList<string> ExtractSupportedUrls(string? text) =>
        ExtractUrls(text)
            .Where(IsSupportedVideoUrl)
            .ToList();

    public static Platform DetectPlatform(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Platform.Unknown;
        }

        var host = uri.Host.ToLowerInvariant();
        if (IsHost(host, "youtube.com") || IsHost(host, "youtu.be"))
        {
            return Platform.YouTube;
        }

        if (IsHost(host, "twitter.com") || IsHost(host, "x.com"))
        {
            return Platform.Twitter;
        }

        if (IsHost(host, "instagram.com"))
        {
            return Platform.Instagram;
        }

        if (IsHost(host, "tiktok.com"))
        {
            return Platform.TikTok;
        }

        if (IsHost(host, "reddit.com") || IsHost(host, "redd.it"))
        {
            return Platform.Reddit;
        }

        return Platform.Unknown;
    }

    public static bool IsSupportedUrl(string? text) => TryExtractFirstSupportedUrl(text, out _);

    public static bool IsSupportedVideoUrl(string? url) =>
        DetectPlatform(url) is not Platform.Unknown;

    private static string CleanUrlCandidate(string text)
    {
        var candidate = text.Trim();
        var delimiter = candidate.IndexOfAny(UrlEndDelimiters);
        if (delimiter >= 0)
        {
            candidate = candidate[..delimiter];
        }

        return candidate.Trim().TrimStart(UrlTrimStart).TrimEnd(UrlTrimEnd);
    }

    private static bool IsHost(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"https?://", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlStartRegex();
}
