using Clip.Core.Platform;

namespace Clip.Platform.MacOS;

public sealed class MacOSBrowserCookieSourceDetector : IBrowserCookieSourceDetector
{
    public IReadOnlyList<string> Detect()
    {
        var applicationSupport = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support");
        var candidates = new (string Browser, string Path)[]
        {
            ("chrome", Path.Combine(applicationSupport, "Google", "Chrome")),
            ("edge", Path.Combine(applicationSupport, "Microsoft Edge")),
            ("brave", Path.Combine(applicationSupport, "BraveSoftware", "Brave-Browser")),
            ("firefox", Path.Combine(applicationSupport, "Firefox", "Profiles"))
        };

        return candidates
            .Where(candidate => Directory.Exists(candidate.Path))
            .Select(candidate => candidate.Browser)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
