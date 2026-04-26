using System.Net.Http.Headers;
using System.Text.Json;

namespace Clip.Services;

public sealed class RedditResolver
{
    private readonly HttpClient _httpClient;

    public RedditResolver(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Clip", "1.0"));
    }

    public async Task<string> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var apiUrl = $"https://api.reddit.com/api/info/?url={Uri.EscapeDataString(url)}";
            using var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (TryResolveFromListing(document.RootElement, out var resolved))
            {
                return resolved;
            }
        }
        catch
        {
            // Fall back to yt-dlp when Reddit's public API is throttled or unavailable.
        }

        return url;
    }

    private static bool TryResolveFromListing(JsonElement root, out string url)
    {
        url = "";
        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("children", out var children) ||
            children.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var child in children.EnumerateArray())
        {
            if (child.TryGetProperty("data", out var childData) &&
                TryResolveFromPost(childData, out url))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveFromPost(JsonElement post, out string url)
    {
        url = "";
        if (TryReadRedditVideo(post, "secure_media", out url) ||
            TryReadRedditVideo(post, "media", out url))
        {
            return true;
        }

        if (post.TryGetProperty("crosspost_parent_list", out var crossposts) &&
            crossposts.ValueKind == JsonValueKind.Array)
        {
            foreach (var crosspost in crossposts.EnumerateArray())
            {
                if (TryResolveFromPost(crosspost, out url))
                {
                    return true;
                }
            }
        }

        if (post.TryGetProperty("url_overridden_by_dest", out var destination) &&
            destination.ValueKind == JsonValueKind.String)
        {
            var value = destination.GetString() ?? "";
            if (value.Contains("v.redd.it", StringComparison.OrdinalIgnoreCase))
            {
                url = value;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadRedditVideo(JsonElement post, string propertyName, out string url)
    {
        url = "";
        if (!post.TryGetProperty(propertyName, out var media) ||
            media.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            !media.TryGetProperty("reddit_video", out var redditVideo) ||
            !redditVideo.TryGetProperty("fallback_url", out var fallback) ||
            fallback.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        url = fallback.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(url);
    }
}
