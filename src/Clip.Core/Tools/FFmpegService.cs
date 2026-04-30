using Clip.Core.App;
using Clip.Core.Ffmpeg;
using Clip.Core.Processes;

namespace Clip.Core.Tools;

public sealed class FFmpegService
{
    private readonly ToolResolver _toolResolver;
    private readonly IExternalProcessRunner _processRunner;
    private readonly IAppSettingsProvider _settingsProvider;

    public FFmpegService(
        ToolResolver toolResolver,
        IExternalProcessRunner processRunner,
        IAppSettingsProvider settingsProvider)
    {
        _toolResolver = toolResolver;
        _processRunner = processRunner;
        _settingsProvider = settingsProvider;
    }

    public async Task TrimAsync(
        string inputPath,
        string outputPath,
        double startSeconds,
        double endSeconds,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = ResolveRequired(ExternalTool.Ffmpeg);
        var settings = _settingsProvider.Current;
        var options = new FfmpegTrimOptions
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            StartSeconds = startSeconds,
            EndSeconds = endSeconds,
            CompressionMode = settings.CompressionMode,
            VideoEncoder = settings.VideoEncoder
        };

        var args = settings.TrimMode == TrimMode.Fast
            ? FfmpegCommandBuilder.BuildFastTrim(options)
            : FfmpegCommandBuilder.BuildExactTrim(options);

        var result = await _processRunner.RunAsync(ffmpeg.Path!, args, cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(FirstErrorLine(result.StandardError, "ffmpeg trim failed."));
        }
    }

    public async Task CompressAsync(
        FfmpegCompressionOptions options,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = ResolveRequired(ExternalTool.Ffmpeg);
        var settings = _settingsProvider.Current;
        var effective = new FfmpegCompressionOptions
        {
            InputPath = options.InputPath,
            OutputPath = options.OutputPath,
            DurationSeconds = options.DurationSeconds,
            TargetMegabytes = options.TargetMegabytes,
            AudioKbps = options.AudioKbps,
            CompressionMode = settings.CompressionMode,
            VideoEncoder = settings.VideoEncoder
        };

        var result = await _processRunner.RunAsync(ffmpeg.Path!, FfmpegCommandBuilder.BuildCompression(effective), cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(FirstErrorLine(result.StandardError, "ffmpeg compression failed."));
        }
    }

    private ExternalToolResolution ResolveRequired(ExternalTool tool)
    {
        var resolved = _toolResolver.Resolve(tool);
        if (!resolved.IsFound || resolved.Path is null)
        {
            throw new FileNotFoundException($"{resolved.DisplayName} was not found.", resolved.DisplayName);
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
