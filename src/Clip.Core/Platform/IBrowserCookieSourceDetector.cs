namespace Clip.Core.Platform;

public interface IBrowserCookieSourceDetector
{
    IReadOnlyList<string> Detect();
}
