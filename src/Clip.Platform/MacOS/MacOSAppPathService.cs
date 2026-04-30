using Clip.Core.App;
using Clip.Core.Platform;
using Clip.Core.Tools;

namespace Clip.Platform.MacOS;

public sealed class MacOSAppPathService : IAppPathService
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string AppDataDirectory =>
        Path.Combine(Home, "Library", "Application Support", ClipPaths.AppName);

    public string DefaultDownloadsDirectory =>
        Path.Combine(Home, "Downloads", ClipPaths.AppName);

    public string ToolsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "bin", HostPlatformDetector.Detect().ResourceFolderName);

    public string LogsDirectory =>
        Path.Combine(Home, "Library", "Logs", ClipPaths.AppName);

    public string MetadataCacheDirectory => Path.Combine(AppDataDirectory, "cache", "metadata");
}
