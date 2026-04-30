namespace Clip.Core.Platform;

public class NullClipboardMonitor : IClipboardMonitor
{
    public bool IsEnabled { get; set; }
    public event EventHandler<string>? SupportedUrlDetected;

    public void Dispose()
    {
    }

    public void EmitForTests(string url) => SupportedUrlDetected?.Invoke(this, url);
}
