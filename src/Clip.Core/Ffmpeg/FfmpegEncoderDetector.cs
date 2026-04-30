using Clip.Core.App;
using Clip.Core.Processes;
using Clip.Core.Tools;

namespace Clip.Core.Ffmpeg;

public sealed class FfmpegEncoderDetector
{
    private readonly IExternalProcessRunner _processRunner;
    private readonly ToolResolver _toolResolver;

    public FfmpegEncoderDetector(IExternalProcessRunner processRunner, ToolResolver toolResolver)
    {
        _processRunner = processRunner;
        _toolResolver = toolResolver;
    }

    public async Task<FfmpegEncoderDetectionResult> DetectAsync(CancellationToken cancellationToken = default)
    {
        var ffmpeg = _toolResolver.Resolve(ExternalTool.Ffmpeg);
        if (!ffmpeg.IsFound || ffmpeg.Path is null)
        {
            return new FfmpegEncoderDetectionResult(new HashSet<string>(StringComparer.OrdinalIgnoreCase), VideoEncoderChoice.SoftwareX264, "ffmpeg was not found.");
        }

        var result = await _processRunner.RunAsync(ffmpeg.Path, ["-hide_banner", "-encoders"], cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            return new FfmpegEncoderDetectionResult(new HashSet<string>(StringComparer.OrdinalIgnoreCase), VideoEncoderChoice.SoftwareX264, "ffmpeg -encoders failed.");
        }

        var encoders = ParseEncoders(result.StandardOutput + Environment.NewLine + result.StandardError);
        var recommended = ChooseRecommendedEncoder(encoders, _toolResolver.Platform);
        return new FfmpegEncoderDetectionResult(encoders, recommended, null);
    }

    public static ISet<string> ParseEncoders(string text)
    {
        var encoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2 && parts[0].Contains('V', StringComparison.Ordinal))
            {
                encoders.Add(parts[1]);
            }
        }

        return encoders;
    }

    public static VideoEncoderChoice ChooseRecommendedEncoder(ISet<string> availableEncoders, HostPlatform platform)
    {
        if (platform.IsMacOS)
        {
            if (availableEncoders.Contains("h264_videotoolbox"))
            {
                return VideoEncoderChoice.AppleH264;
            }

            if (availableEncoders.Contains("hevc_videotoolbox"))
            {
                return VideoEncoderChoice.AppleHevc;
            }
        }

        if (platform.IsWindows)
        {
            if (availableEncoders.Contains("h264_nvenc"))
            {
                return VideoEncoderChoice.NvidiaH264;
            }

            if (availableEncoders.Contains("h264_qsv"))
            {
                return VideoEncoderChoice.IntelH264;
            }

            if (availableEncoders.Contains("h264_amf"))
            {
                return VideoEncoderChoice.AmdH264;
            }
        }

        return VideoEncoderChoice.SoftwareX264;
    }

    public static bool IsEncoderAvailable(VideoEncoderChoice choice, ISet<string> availableEncoders)
    {
        var codec = FfmpegCommandBuilder.ToFfmpegCodec(choice);
        return codec is "libx264" or "libx265" || availableEncoders.Contains(codec);
    }
}

public sealed record FfmpegEncoderDetectionResult(
    ISet<string> AvailableEncoders,
    VideoEncoderChoice RecommendedEncoder,
    string? Warning);
