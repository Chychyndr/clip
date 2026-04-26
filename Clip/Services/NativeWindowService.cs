using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Clip.Services;

public static class NativeWindowService
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    public static IntPtr GetWindowHandle(Window window) => WindowNative.GetWindowHandle(window);

    public static AppWindow GetAppWindow(Window window)
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(GetWindowHandle(window));
        return AppWindow.GetFromWindowId(windowId);
    }

    public static void Resize(Window window, int width, int height)
    {
        GetAppWindow(window).Resize(new SizeInt32(width, height));
    }

    public static void Hide(Window window) => ShowWindow(GetWindowHandle(window), SwHide);

    public static void ShowAndActivate(Window window)
    {
        var handle = GetWindowHandle(window);
        ShowWindow(handle, SwShow);
        ShowWindow(handle, SwRestore);
        SetForegroundWindow(handle);
        window.Activate();
    }

    public static bool IsVisible(Window window) => IsWindowVisible(GetWindowHandle(window));

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}
