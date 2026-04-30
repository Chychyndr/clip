using Clip.Core.Platform;
using System.Diagnostics;

namespace Clip.Platform.MacOS;

public sealed class MacOSClipboardMonitor : NullClipboardMonitor
{
    public MacOSClipboardMonitor()
    {
        Debug.WriteLine("macOS clipboard monitor is not implemented yet; using a safe stub.");
    }
}
