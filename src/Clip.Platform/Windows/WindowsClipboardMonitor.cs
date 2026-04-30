using Clip.Core.Platform;
using System.Diagnostics;

namespace Clip.Platform.Windows;

public sealed class WindowsClipboardMonitor : NullClipboardMonitor
{
    public WindowsClipboardMonitor()
    {
        Debug.WriteLine("Windows clipboard monitor is using the safe cross-platform stub.");
    }
}
