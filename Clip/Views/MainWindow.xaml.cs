using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Clip.Services;
using Clip.ViewModels;

namespace Clip.Views;

public sealed partial class MainWindow : Window
{
    private const int InitialWidth = 1480;
    private const int InitialHeight = 940;
    private DispatcherQueueTimer? _initialSizeTimer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        Root.DataContext = ViewModel;
        ClipTheme.ApplyMica(this);
        var appWindow = NativeWindowService.GetAppWindow(this);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Clip.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
            NativeWindowService.SetWindowIcon(this, iconPath);
        }

        appWindow.Closing += OnAppWindowClosing;
    }

    public MainViewModel ViewModel { get; }
    public bool AllowClose { get; set; }

    public void ApplyInitialSize() => NativeWindowService.Resize(this, InitialWidth, InitialHeight);

    public void ScheduleInitialSize()
    {
        _initialSizeTimer?.Stop();
        _initialSizeTimer = DispatcherQueue.CreateTimer();
        _initialSizeTimer.Interval = TimeSpan.FromMilliseconds(250);
        _initialSizeTimer.IsRepeating = false;
        _initialSizeTimer.Tick += OnInitialSizeTimerTick;
        _initialSizeTimer.Start();
    }

    private void OnInitialSizeTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        sender.Tick -= OnInitialSizeTimerTick;
        _initialSizeTimer = null;
        ApplyInitialSize();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (AllowClose || !ViewModel.Settings.HideToTrayOnClose)
        {
            return;
        }

        args.Cancel = true;
        NativeWindowService.Hide(this);
    }
}
