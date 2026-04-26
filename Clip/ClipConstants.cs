namespace Clip;

public static class ClipConstants
{
    public const string AppName = "Clip";
    public const string SingleInstanceName = "Local\\Clip.SingleInstance";
    public const string IpcPipeName = "Clip.SecondInstancePipe";
    public const int MaxConcurrentDownloads = 3;

    public static string AppBaseDirectory => AppContext.BaseDirectory;
    public static string BinDirectory => Path.Combine(AppBaseDirectory, "Resources", "bin");
    public static string YtDlpPath => Path.Combine(BinDirectory, "yt-dlp.exe");
    public static string FFmpegPath => Path.Combine(BinDirectory, "ffmpeg.exe");
    public static string FFprobePath => Path.Combine(BinDirectory, "ffprobe.exe");

    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string HistoryPath => Path.Combine(AppDataDirectory, "history.json");
    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");

    public static string DefaultDownloadDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", AppName);

    public static IReadOnlyList<string> ExtraProbePaths { get; } =
    [
        @"C:\ffmpeg\bin",
        @"C:\Program Files\ffmpeg\bin",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages")
    ];

    public static void EnsureAppDirectories()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(DefaultDownloadDirectory);
    }
}
