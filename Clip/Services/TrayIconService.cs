using System.Runtime.InteropServices;
using Clip.ViewModels;
using Clip.Views;

namespace Clip.Services;

public sealed class TrayIconService : IDisposable
{
    private const int TrayIconId = 1;
    private const int CallbackMessage = 0x8000 + 0x122;
    private const int NifMessage = 0x00000001;
    private const int NifIcon = 0x00000002;
    private const int NifTip = 0x00000004;
    private const int NimAdd = 0x00000000;
    private const int NimModify = 0x00000001;
    private const int NimDelete = 0x00000002;
    private const int NimSetVersion = 0x00000004;
    private const int NotifyIconVersion4 = 4;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int GwlWndProc = -4;
    private const int IdiApplication = 32512;
    private const int MfString = 0x00000000;
    private const int MfSeparator = 0x00000800;
    private const int TpmRightButton = 0x0002;
    private const int TpmReturnCommand = 0x0100;
    private const int ImageIcon = 1;
    private const int LrLoadFromFile = 0x00000010;
    private const int LrDefaultSize = 0x00000040;

    private readonly MainWindow _window;
    private readonly MainViewModel _viewModel;
    private readonly IntPtr _windowHandle;
    private readonly IntPtr _iconHandle;
    private readonly bool _ownsIconHandle;
    private readonly WindowProcedure _newWindowProcedure;
    private IntPtr _oldWindowProcedure;
    private bool _isDisposed;

    public TrayIconService(MainWindow window, MainViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;
        _windowHandle = NativeWindowService.GetWindowHandle(window);
        (_iconHandle, _ownsIconHandle) = LoadTrayIcon();
        _newWindowProcedure = WndProc;
        _oldWindowProcedure = SetWindowProcedure(_windowHandle, _newWindowProcedure);

        AddOrUpdateIcon("Clip is ready", NimAdd);
        var data = CreateNotifyIconData("Clip is ready");
        data.uVersion = NotifyIconVersion4;
        ShellNotifyIcon(NimSetVersion, ref data);
    }

    public void SetBusy(bool isBusy)
    {
        AddOrUpdateIcon(isBusy ? "Clip is downloading" : "Clip is ready", NimModify);
    }

    private IntPtr WndProc(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage && wParam.ToUInt32() == TrayIconId)
        {
            var mouseMessage = lParam.ToInt32();
            if (mouseMessage == WmLButtonUp)
            {
                ToggleWindow();
                return IntPtr.Zero;
            }

            if (mouseMessage == WmRButtonUp)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(_oldWindowProcedure, hWnd, message, wParam, lParam);
    }

    private void AddOrUpdateIcon(string tooltip, int message)
    {
        var data = CreateNotifyIconData(tooltip);
        ShellNotifyIcon(message, ref data);
    }

    private NotifyIconData CreateNotifyIconData(string tooltip)
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = CallbackMessage,
            hIcon = _iconHandle,
            szTip = tooltip
        };
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, 1, "Развернуть Clip");
            AppendMenu(menu, MfString, 2, "Скрыть Clip");
            AppendMenu(menu, MfString, 3, "Вставить ссылку");
            AppendMenu(menu, MfString, 4, "Начать загрузку");
            AppendMenu(menu, MfString, 5, _viewModel.Downloads.IsPaused ? "Продолжить очередь" : "Поставить очередь на паузу");
            AppendMenu(menu, MfString, 6, "Настройки");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, 7, "Отключить Clip");

            GetCursorPos(out var point);
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand, point.X, point.Y, 0, _windowHandle, IntPtr.Zero);
            _ = HandleMenuCommandAsync(command);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private async Task HandleMenuCommandAsync(int command)
    {
        switch (command)
        {
            case 1:
                ShowWindow();
                break;
            case 2:
                NativeWindowService.Hide(_window);
                break;
            case 3:
                ShowWindow();
                await _viewModel.PasteFromClipboardAsync();
                break;
            case 4:
                ShowWindow();
                await _viewModel.QueueCurrentDownloadAsync();
                break;
            case 5:
                _viewModel.Downloads.TogglePause();
                break;
            case 6:
                _viewModel.ShowSettings = true;
                ShowWindow();
                break;
            case 7:
                _window.AllowClose = true;
                Dispose();
                _window.Close();
                break;
        }
    }

    private void ToggleWindow()
    {
        if (NativeWindowService.IsVisible(_window))
        {
            NativeWindowService.Hide(_window);
        }
        else
        {
            ShowWindow();
        }
    }

    private void ShowWindow() => NativeWindowService.ShowAndActivate(_window);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        var data = CreateNotifyIconData("");
        ShellNotifyIcon(NimDelete, ref data);

        if (_oldWindowProcedure != IntPtr.Zero)
        {
            SetWindowProcedure(_windowHandle, _oldWindowProcedure);
            _oldWindowProcedure = IntPtr.Zero;
        }

        if (_ownsIconHandle && _iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
    }

    private static (IntPtr Handle, bool ShouldDestroy) LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Clip.ico");
        if (File.Exists(iconPath))
        {
            var handle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            if (handle != IntPtr.Zero)
            {
                return (handle, true);
            }
        }

        return (LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication)), false);
    }

    private static IntPtr SetWindowProcedure(IntPtr windowHandle, WindowProcedure procedure)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr(windowHandle, GwlWndProc, Marshal.GetFunctionPointerForDelegate(procedure))
            : SetWindowLong(windowHandle, GwlWndProc, Marshal.GetFunctionPointerForDelegate(procedure));
    }

    private static IntPtr SetWindowProcedure(IntPtr windowHandle, IntPtr procedure)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr(windowHandle, GwlWndProc, procedure)
            : SetWindowLong(windowHandle, GwlWndProc, procedure);
    }

    private delegate IntPtr WindowProcedure(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public int dwState;
        public int dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public int uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, int type, int width, int height, int load);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong(IntPtr hWnd, int index, IntPtr newLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr previousProcedure, IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, int flags, int newItemId, string? newItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, int flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rectangle);
}
