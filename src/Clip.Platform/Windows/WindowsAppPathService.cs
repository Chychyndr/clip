using Clip.Core.App;
using Clip.Core.Platform;
using Clip.Core.Tools;

namespace Clip.Platform.Windows;

public sealed class WindowsAppPathService : IAppPathService
{
    public string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ClipPaths.AppName);

    public string DefaultDownloadsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", ClipPaths.AppName);

    public string ToolsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "bin", HostPlatformDetector.Detect().ResourceFolderName);

    public string LogsDirectory => Path.Combine(AppDataDirectory, "logs");

    public string MetadataCacheDirectory => Path.Combine(AppDataDirectory, "cache", "metadata");
}
