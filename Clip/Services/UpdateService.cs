using System.Net.Http.Headers;
using System.Text.Json;

namespace Clip.Services;

public sealed class UpdateService
{
    private readonly ProcessRunner _processRunner;
    private readonly HttpClient _httpClient;

    public UpdateService(ProcessRunner processRunner, HttpClient? httpClient = null)
    {
        _processRunner = processRunner;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Clip", "1.0"));
    }

    public async Task<string?> CheckForYtDlpUpdateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ClipConstants.YtDlpPath))
        {
            return "yt-dlp.exe is missing from Resources\\bin.";
        }

        var local = await ReadLocalVersionAsync(cancellationToken);
        var latest = await ReadLatestVersionAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(local) || string.IsNullOrWhiteSpace(latest))
        {
            return null;
        }

        return string.Equals(local, latest, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"yt-dlp {latest} is available. Bundled version: {local}.";
    }

    private async Task<string?> ReadLocalVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                ClipConstants.YtDlpPath,
                ["--version"],
                cancellationToken: cancellationToken);
            return result.IsSuccess ? result.StandardOutput.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ReadLatestVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest", cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("tag_name", out var tag)
                ? tag.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
