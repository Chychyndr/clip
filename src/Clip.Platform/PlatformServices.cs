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
        new NullClipboardMonitor();

    public static ITrayService CreateTrayService() =>
        new NullTrayService();
}
