namespace Clip.Core.Platform;

public interface IClipboardMonitor : IDisposable
{
    bool IsEnabled { get; set; }
    event EventHandler<string>? SupportedUrlDetected;
}
