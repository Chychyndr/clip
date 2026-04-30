using System.Runtime.InteropServices;
using Clip.Core.Platform;
using Clip.Platform.MacOS;
using Clip.Platform.Windows;

namespace Clip.Platform;

public static class PlatformServices
{
    public static IAppPathService CreatePathService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOSAppPathService();
        }

        return new WindowsAppPathService();
    }

    public static IBrowserCookieSourceDetector CreateBrowserCookieSourceDetector()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOSBrowserCookieSourceDetector();
        }

        return new WindowsBrowserCookieSourceDetector();
    }

    public static IClipboardMonitor CreateClipboardMonitor() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new MacOSClipboardMonitor()
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsClipboardMonitor()
                : new NullClipboardMonitor();

    public static ITrayService CreateTrayService() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new MacOSTrayService()
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new WindowsTrayService()
                : new NullTrayService();
}
