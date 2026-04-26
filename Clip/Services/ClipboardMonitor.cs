using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace Clip.Services;

public sealed class ClipboardMonitor : IDisposable
{
    private readonly DispatcherQueueTimer _timer;
    private string _lastUrl = "";
    private bool _isDisposed;

    public ClipboardMonitor(DispatcherQueue dispatcherQueue)
    {
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.5);
        _timer.Tick += async (_, _) => await CheckClipboardAsync();
    }

    public event EventHandler<string>? SupportedUrlDetected;

    public bool IsEnabled
    {
        get => _timer.IsRunning;
        set
        {
            if (value && !_timer.IsRunning)
            {
                _timer.Start();
            }
            else if (!value && _timer.IsRunning)
            {
                _timer.Stop();
            }
        }
    }

    private async Task CheckClipboardAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            var content = Clipboard.GetContent();
            var text = "";

            if (content.Contains(StandardDataFormats.WebLink))
            {
                var webLink = await content.GetWebLinkAsync();
                text = webLink?.ToString() ?? "";
            }
            else if (content.Contains(StandardDataFormats.Text))
            {
                text = await content.GetTextAsync();
            }

            if (!URLDetector.TryExtractFirstUrl(text, out var url) ||
                string.Equals(url, _lastUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastUrl = url;
            SupportedUrlDetected?.Invoke(this, url);
        }
        catch
        {
            // Clipboard access can fail transiently when another app owns it.
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        _timer.Stop();
    }
}
