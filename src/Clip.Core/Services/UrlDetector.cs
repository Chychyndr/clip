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

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        if (IsDomainOrSubdomain(host, "youtube.com") || IsDomainOrSubdomain(host, "youtu.be"))
        {
            return ClipPlatform.YouTube;
        }

        if (IsDomainOrSubdomain(host, "twitter.com") || IsDomainOrSubdomain(host, "x.com"))
        {
            return ClipPlatform.Twitter;
        }

        if (IsDomainOrSubdomain(host, "instagram.com"))
        {
            return ClipPlatform.Instagram;
        }

        if (IsDomainOrSubdomain(host, "tiktok.com"))
        {
            return ClipPlatform.TikTok;
        }

        if (IsDomainOrSubdomain(host, "reddit.com") || IsDomainOrSubdomain(host, "redd.it"))
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

    public static bool IsSupportedVideoUrl(string url) => DetectPlatform(url) is not ClipPlatform.Unknown;

    private static bool IsDomainOrSubdomain(string host, string domain)
    {
        domain = domain.ToLowerInvariant();
        return host.Equals(domain, StringComparison.Ordinal) ||
               host.EndsWith("." + domain, StringComparison.Ordinal);
    }
}
