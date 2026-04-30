using Clip.Core.Platform;
using System.Diagnostics;

namespace Clip.Platform.MacOS;

public sealed class MacOSTrayService : NullTrayService
{
    public MacOSTrayService()
    {
        Debug.WriteLine("macOS tray service is not implemented yet; using a safe stub.");
    }
}
