using Clip.Core.Platform;

namespace Clip.Platform.Windows;

public sealed class WindowsBrowserCookieSourceDetector : IBrowserCookieSourceDetector
{
    public IReadOnlyList<string> Detect()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidates = new (string Browser, string Path)[]
        {
            ("chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data")),
            ("edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data")),
            ("brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data")),
            ("firefox", Path.Combine(roamingAppData, "Mozilla", "Firefox", "Profiles"))
        };

        return candidates
            .Where(candidate => Directory.Exists(candidate.Path))
            .Select(candidate => candidate.Browser)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
