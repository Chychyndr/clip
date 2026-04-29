using Clip.Core.App;
using Clip.Core.Tools;

namespace Clip;

public static class ClipConstants
{
    public const string AppName = "Clip";
    public const string SingleInstanceName = "Local\\Clip.SingleInstance";
    public const string IpcPipeName = "Clip.SecondInstancePipe";

    public static string AppBaseDirectory => AppContext.BaseDirectory;
    public static string BinDirectory => Path.Combine(AppBaseDirectory, "Resources", "bin", HostPlatformDetector.Detect().RuntimeIdentifier);
    public static string LegacyBinDirectory => Path.Combine(AppBaseDirectory, "Resources", "bin");
    public static string YtDlpPath => new ToolResolver(AppBaseDirectory).Resolve(ExternalTool.YtDlp).Path
        ?? Path.Combine(BinDirectory, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");
    public static string FFmpegPath => new ToolResolver(AppBaseDirectory).Resolve(ExternalTool.Ffmpeg).Path
        ?? Path.Combine(BinDirectory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    public static string FFprobePath => new ToolResolver(AppBaseDirectory).Resolve(ExternalTool.Ffprobe).Path
        ?? Path.Combine(BinDirectory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");

    public static string AppDataDirectory => ClipPaths.AppDataDirectory;
    public static string LogDirectory => ClipPaths.LogDirectory;
    public static string LogPath => ClipPaths.LogPath;
    public static string MetadataCacheDirectory => ClipPaths.MetadataCacheDirectory;

    public static string HistoryPath => ClipPaths.HistoryPath;
    public static string SettingsPath => ClipPaths.SettingsPath;

    public static string DefaultDownloadDirectory => ClipPaths.DefaultDownloadDirectory;

    public static IReadOnlyList<string> ExtraProbePaths { get; } =
    [
        @"C:\ffmpeg\bin",
        @"C:\Program Files\ffmpeg\bin",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages")
    ];

    public static void EnsureAppDirectories()
    {
        ClipPaths.EnsureUserDirectories();
    }
}
