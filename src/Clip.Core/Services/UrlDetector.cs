using Clip.Core.Models;
using ClipPlatform = Clip.Core.Models.Platform;

namespace Clip.Core.Services;

public static class UrlDetector
{
    public static bool TryNormalize(string? text, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        url = uri.ToString();
        return true;
    }

    public static ClipPlatform DetectPlatform(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return ClipPlatform.Unknown;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.Contains("youtube.com", StringComparison.Ordinal) || host.Contains("youtu.be", StringComparison.Ordinal))
        {
            return ClipPlatform.YouTube;
        }

        if (host.Contains("twitter.com", StringComparison.Ordinal) || host.Contains("x.com", StringComparison.Ordinal))
        {
            return ClipPlatform.Twitter;
        }

        if (host.Contains("instagram.com", StringComparison.Ordinal))
        {
            return ClipPlatform.Instagram;
        }

        if (host.Contains("tiktok.com", StringComparison.Ordinal))
        {
            return ClipPlatform.TikTok;
        }

        if (host.Contains("reddit.com", StringComparison.Ordinal))
        {
            return ClipPlatform.Reddit;
        }

        return ClipPlatform.Unknown;
    }

    public static IReadOnlyList<string> ExtractDistinctUrls(IEnumerable<string> lines)
    {
        var urls = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (!TryNormalize(line, out var url) || !seen.Add(url))
            {
                continue;
            }

            urls.Add(url);
        }

        return urls;
    }
}
