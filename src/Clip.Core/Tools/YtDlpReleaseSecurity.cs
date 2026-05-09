// SPDX-License-Identifier: GPL-3.0-only

using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Clip.Core.Tools;

public static class YtDlpReleaseSecurity
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(30);

    private const string TrustedReleasePrefix = "https://github.com/yt-dlp/yt-dlp/releases/download/";

    public static void ConfigureHttpClient(HttpClient httpClient)
    {
        try
        {
            httpClient.Timeout = HttpTimeout;
        }
        catch (InvalidOperationException)
        {
            // An injected HttpClient may already be in use; callers can still cancel requests.
        }
    }

    public static void EnsureTrustedReleaseAssetUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !url.StartsWith(TrustedReleasePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException("yt-dlp release asset URL is not trusted.");
        }
    }

    public static string? SelectWindowsBinaryUrl(IEnumerable<YtDlpReleaseAsset> assets) =>
        assets.FirstOrDefault(asset => asset.Name.Equals("yt-dlp.exe", StringComparison.OrdinalIgnoreCase))?.DownloadUrl;

    public static string? SelectChecksumUrl(IEnumerable<YtDlpReleaseAsset> assets)
    {
        string[] preferred =
        [
            "SHA2-256SUMS",
            "SHA2-256SUMS.txt",
            "SHA256SUMS",
            "SHA256SUMS.txt"
        ];

        return preferred
            .Select(name => assets.FirstOrDefault(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(asset => asset is not null)
            ?.DownloadUrl;
    }

    public static async Task<byte[]> DownloadVerifiedAssetAsync(
        HttpClient httpClient,
        string assetName,
        string assetUrl,
        string checksumUrl,
        CancellationToken cancellationToken)
    {
        EnsureTrustedReleaseAssetUrl(assetUrl);
        EnsureTrustedReleaseAssetUrl(checksumUrl);

        var checksumText = await httpClient.GetStringAsync(checksumUrl, cancellationToken);
        var expectedSha256 = ParseExpectedSha256(checksumText, assetName);
        if (expectedSha256 is null)
        {
            throw new SecurityException($"yt-dlp checksum file does not contain {assetName}.");
        }

        var bytes = await httpClient.GetByteArrayAsync(assetUrl, cancellationToken);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var expectedBytes = Encoding.ASCII.GetBytes(expectedSha256);
        var actualBytes = Encoding.ASCII.GetBytes(actualSha256);
        if (expectedBytes.Length != actualBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            throw new SecurityException("Downloaded yt-dlp checksum mismatch.");
        }

        return bytes;
    }

    private static string? ParseExpectedSha256(string checksumText, string assetName)
    {
        foreach (var rawLine in checksumText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = rawLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var hash = parts[0].Trim();
            var name = parts[^1].TrimStart('*');
            if (hash.Length == 64 &&
                hash.All(Uri.IsHexDigit) &&
                Path.GetFileName(name).Equals(assetName, StringComparison.OrdinalIgnoreCase))
            {
                return hash.ToLowerInvariant();
            }
        }

        return null;
    }
}

public sealed record YtDlpReleaseAsset(string Name, string DownloadUrl);
