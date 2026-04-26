using Clip.ViewModels;
using Clip.Views;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Clip.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly MainWindow _window;
    private readonly MainViewModel _viewModel;

    public TrayIconService(MainWindow window, MainViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Clip", null, (_, _) => ShowWindow());
        menu.Items.Add("Paste URL", null, async (_, _) =>
        {
            ShowWindow();
            await _viewModel.PasteFromClipboardAsync();
        });
        menu.Items.Add("Start Download", null, async (_, _) =>
        {
            ShowWindow();
            await _viewModel.QueueCurrentDownloadAsync();
        });
        menu.Items.Add("Pause Queue", null, (_, _) => _viewModel.Downloads.TogglePause());
        menu.Items.Add("Settings", null, (_, _) =>
        {
            _viewModel.ShowSettings = true;
            ShowWindow();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) =>
        {
            _window.AllowClose = true;
            Dispose();
            _window.Close();
        });

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "Clip is ready",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                ToggleWindow();
            }
        };
    }

    public void SetBusy(bool isBusy)
    {
        _notifyIcon.Text = isBusy ? "Clip is downloading" : "Clip is ready";
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
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
