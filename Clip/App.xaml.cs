using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Clip.Models;
using Clip.Services;
using Clip.ViewModels;
using Clip.Views;

namespace Clip;

public partial class App : Application
{
    private SingleInstanceService? _singleInstanceService;
    private TrayIconService? _trayIconService;
    private ClipboardMonitor? _clipboardMonitor;
    private MainViewModel? _viewModel;
    private MainWindow? _window;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                CrashLog.Error(exception, "AppDomain unhandled exception");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            CrashLog.Error(eventArgs.Exception, "Unobserved task exception");
            eventArgs.SetObserved();
        };
        UnhandledException += (_, eventArgs) =>
        {
            CrashLog.Error(eventArgs.Exception, "WinUI unhandled exception");
        };

        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            CrashLog.Info("Launch started");
            ClipConstants.EnsureAppDirectories();
            _singleInstanceService = new SingleInstanceService(ClipConstants.SingleInstanceName, ClipConstants.IpcPipeName);

            var launchCommand = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
            if (!_singleInstanceService.TryClaim())
            {
                var command = string.IsNullOrWhiteSpace(launchCommand) ? "clip:show" : launchCommand;
                await SingleInstanceService.SendToExistingInstanceAsync(ClipConstants.IpcPipeName, command);
                Exit();
                return;
            }

            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            var processRunner = new ProcessRunner();
            var redditResolver = new RedditResolver();
            var ytDlpService = new YTDLPService(processRunner, redditResolver);
            var ffmpegService = new FFmpegService(processRunner);
            var updateService = new UpdateService(processRunner);
            var fileDialogService = new FileDialogService();
            var outputPathHolder = new OutputPathHolder();
            var settings = SettingsViewModel.Load();
            var history = DownloadHistory.Load(ClipConstants.HistoryPath);
            var downloads = new DownloadViewModel(ytDlpService, ffmpegService, history, dispatcherQueue);

            _viewModel = new MainViewModel(
                ytDlpService,
                fileDialogService,
                outputPathHolder,
                updateService,
                downloads,
                settings);

            CrashLog.Info("Creating main window");
            _window = new MainWindow(_viewModel);
            _viewModel.WindowHandle = NativeWindowService.GetWindowHandle(_window);
            _window.Closed += (_, _) => DisposeServices();

            CrashLog.Info("Creating tray icon");
            _trayIconService = new TrayIconService(_window, _viewModel);
            downloads.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(DownloadViewModel.IsBusy))
                {
                    _trayIconService.SetBusy(downloads.IsBusy);
                }
            };

            _clipboardMonitor = new ClipboardMonitor(dispatcherQueue)
            {
                IsEnabled = settings.MonitorClipboard
            };
            _clipboardMonitor.SupportedUrlDetected += (_, url) =>
                dispatcherQueue.TryEnqueue(async () => await _viewModel.AcceptDetectedClipboardUrlAsync(url));
            settings.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(SettingsViewModel.MonitorClipboard) && _clipboardMonitor is not null)
                {
                    _clipboardMonitor.IsEnabled = settings.MonitorClipboard;
                }
            };

            _singleInstanceService.StartListening(command =>
                dispatcherQueue.TryEnqueue(async () =>
                {
                    if (_window is not null && _viewModel is not null)
                    {
                        NativeWindowService.ShowAndActivate(_window);
                        await _viewModel.HandleCommandLineAsync(command);
                    }
                }));

            CrashLog.Info("Activating main window");
            _window.Activate();
            _window.ApplyInitialSize();
            _window.ScheduleInitialSize();
            if (settings.StartMinimized)
            {
                NativeWindowService.Hide(_window);
            }

            await _viewModel.InitializeAsync();
            if (URLDetector.TryExtractFirstSupportedUrl(launchCommand, out _))
            {
                await _viewModel.HandleCommandLineAsync(launchCommand);
            }

            _window.ApplyInitialSize();
            _window.ScheduleInitialSize();

            CrashLog.Info("Launch completed");
        }
        catch (Exception exception)
        {
            CrashLog.Error(exception, "Launch failed");
            throw;
        }
    }

    private void DisposeServices()
    {
        _clipboardMonitor?.Dispose();
        _trayIconService?.Dispose();
        _singleInstanceService?.Dispose();
        _viewModel?.Dispose();
    }
}
