using System.Text.Json;
using Clip.Core.Processes;

namespace Clip.Core.Tools;

public sealed class FFprobeService
{
    private readonly ToolResolver _toolResolver;
    private readonly IExternalProcessRunner _processRunner;

    public FFprobeService(ToolResolver toolResolver, IExternalProcessRunner processRunner)
    {
        _toolResolver = toolResolver;
        _processRunner = processRunner;
    }

    public async Task<double?> GetDurationSecondsAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var ffprobe = ResolveRequired();

        var args = new[]
        {
            "-v",
            "error",
            "-show_entries",
            "format=duration",
            "-of",
            "json",
            inputPath
        };

        var result = await _processRunner.RunAsync(ffprobe.Path!, args, cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(FirstErrorLine(result.StandardError, "ffprobe failed to read media information."));
        }

        using var document = JsonDocument.Parse(result.StandardOutput);
        if (document.RootElement.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var durationElement) &&
            double.TryParse(durationElement.GetString(), out var duration))
        {
            return duration;
        }

        return null;
    }

    private ExternalToolResolution ResolveRequired()
    {
        var resolved = _toolResolver.Resolve(ExternalTool.Ffprobe);
        if (!resolved.IsFound || resolved.Path is null)
        {
            throw new FileNotFoundException("ffprobe was not found.", "ffprobe");
        }

        if (!string.IsNullOrWhiteSpace(resolved.Message))
        {
            throw new InvalidOperationException(resolved.Message);
        }

        return resolved;
    }

    private static string FirstErrorLine(string text, string fallback) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? fallback;
}
