using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Clip.Services;

public static class NativeWindowService
{
    private const string WinUiDesktopWindowClass = "WinUIDesktopWin32WindowClass";
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const int GaRoot = 2;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static IntPtr GetWindowHandle(Window window) => WindowNative.GetWindowHandle(window);

    public static AppWindow GetAppWindow(Window window)
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(GetTopLevelWindowHandle(window));
        return AppWindow.GetFromWindowId(windowId);
    }

    public static void Resize(Window window, int width, int height)
    {
        var windowHandle = GetTopLevelWindowHandle(window);
        SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            SwpNoMove | SwpNoZOrder | SwpNoActivate);
    }

    public static void Hide(Window window) => Hide(GetTopLevelWindowHandle(window));

    public static void Hide(IntPtr windowHandle) => ShowWindow(GetTopLevelWindowHandle(windowHandle), SwHide);

    public static void ShowAndActivate(Window window)
    {
        ShowAndActivate(window, GetTopLevelWindowHandle(window));
    }

    public static void ShowAndActivate(Window window, IntPtr windowHandle)
    {
        windowHandle = GetTopLevelWindowHandle(windowHandle);
        ShowWindow(windowHandle, SwShow);
        ShowWindow(windowHandle, SwRestore);
        SetForegroundWindow(windowHandle);
        window.Activate();
    }

    public static bool IsVisible(Window window) => IsVisible(GetTopLevelWindowHandle(window));

    public static bool IsVisible(IntPtr windowHandle) => IsWindowVisible(GetTopLevelWindowHandle(windowHandle));

    private static IntPtr GetTopLevelWindowHandle(Window window) => GetTopLevelWindowHandle(GetWindowHandle(window));

    private static IntPtr GetTopLevelWindowHandle(IntPtr windowHandle)
    {
        var processWindowHandle = FindCurrentProcessWinUiWindow();
        if (processWindowHandle != IntPtr.Zero)
        {
            return processWindowHandle;
        }

        var topLevelHandle = GetAncestor(windowHandle, GaRoot);
        return topLevelHandle == IntPtr.Zero ? windowHandle : topLevelHandle;
    }

    private static IntPtr FindCurrentProcessWinUiWindow()
    {
        var currentProcessId = (uint)Environment.ProcessId;
        var result = IntPtr.Zero;

        EnumWindows((windowHandle, _) =>
        {
            GetWindowThreadProcessId(windowHandle, out var processId);
            if (processId == currentProcessId && HasClassName(windowHandle, WinUiDesktopWindowClass))
            {
                result = windowHandle;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool HasClassName(IntPtr windowHandle, string className)
    {
        var builder = new StringBuilder(256);
        if (GetClassName(windowHandle, builder, builder.Capacity) == 0)
        {
            return false;
        }

        return string.Equals(builder.ToString(), className, StringComparison.Ordinal);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProcedure enumProcedure, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    private delegate bool EnumWindowsProcedure(IntPtr hWnd, IntPtr lParam);
}
