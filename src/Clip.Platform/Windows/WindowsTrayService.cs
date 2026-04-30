using Clip.Core.Platform;
using System.Diagnostics;

namespace Clip.Platform.Windows;

public sealed class WindowsTrayService : NullTrayService
{
    public WindowsTrayService()
    {
        Debug.WriteLine("Windows tray service is using the safe cross-platform stub.");
    }
}
