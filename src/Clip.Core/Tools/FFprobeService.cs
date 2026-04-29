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
        var ffprobe = _toolResolver.Resolve(ExternalTool.Ffprobe);
        if (!ffprobe.IsFound || ffprobe.Path is null)
        {
            throw new FileNotFoundException("ffprobe was not found.", "ffprobe");
        }

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

        var result = await _processRunner.RunAsync(ffprobe.Path, args, cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("ffprobe failed to read media information.");
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
}
