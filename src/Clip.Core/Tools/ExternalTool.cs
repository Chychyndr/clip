namespace Clip.Core.Tools;

public enum ExternalTool
{
    YtDlp,
    Ffmpeg,
    Ffprobe,
    Aria2c
}

public sealed record ExternalToolResolution(
    ExternalTool Tool,
    string DisplayName,
    string? Path,
    bool IsFound,
    string? Message = null,
    bool IsFromPath = false)
{
    public static ExternalToolResolution Missing(ExternalTool tool, string displayName, string message) =>
        new(tool, displayName, null, false, message);

    public static ExternalToolResolution Found(ExternalTool tool, string displayName, string path, bool isFromPath, string? message = null) =>
        new(tool, displayName, path, true, message, isFromPath);
}
