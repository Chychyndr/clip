namespace Clip.Core.App;

public static class ClipPaths
{
    public const string AppName = "Clip";

    public static string AppDataDirectory => GetAppDataDirectory();
    public static string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");
    public static string HistoryPath => Path.Combine(AppDataDirectory, "history.json");
    public static string CacheDirectory => Path.Combine(AppDataDirectory, "cache");
    public static string MetadataCacheDirectory => Path.Combine(CacheDirectory, "metadata");
    public static string LogDirectory => GetLogDirectory();
    public static string LogPath => Path.Combine(LogDirectory, "clip.log");

    public static string DefaultDownloadDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", AppName);

    public static string GetAppDataDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                AppName);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            return Path.Combine(local, AppName);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppName);
    }

    public static string GetLogDirectory()
    {
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Logs",
                AppName);
        }

        return Path.Combine(AppDataDirectory, "logs");
    }

    public static void EnsureUserDirectories()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(MetadataCacheDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(DefaultDownloadDirectory);
    }
}
