using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Clip.App.Platform;
using Clip.Core.Cache;
using Clip.Core.DownloadQueue;
using Clip.Core.History;
using Clip.Core.Platform;
using Clip.Core.Processes;
using Clip.Core.Settings;
using Clip.Core.Tools;
using Clip.Core.ViewModels;
using Clip.Platform;

namespace Clip.App;

public partial class MainWindow : Window
{
    private readonly AppViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        ApplyWindowIcon();

        var pathService = PlatformServices.CreatePathService();
        Directory.CreateDirectory(pathService.AppDataDirectory);
        Directory.CreateDirectory(pathService.DefaultDownloadsDirectory);
        Directory.CreateDirectory(pathService.LogsDirectory);
        Directory.CreateDirectory(pathService.MetadataCacheDirectory);

        var settingsStore = new SettingsStore(Path.Combine(pathService.AppDataDirectory, "settings.json"));
        var processRunner = new ProcessRunner();
        var toolResolver = new ToolResolver(AppContext.BaseDirectory);
        var metadataCache = new MetadataCacheService(pathService.MetadataCacheDirectory);
        var historyStore = new DownloadHistoryStore(Path.Combine(pathService.AppDataDirectory, "history.json"));
        var ytDlpService = new YtDlpService(toolResolver, processRunner, metadataCache, settingsStore);
        var ffmpegService = new FFmpegService(toolResolver, processRunner, settingsStore);
        var updateService = new YtDlpUpdateService(processRunner, toolResolver, metadataCache);
        var queueService = new DownloadQueueService(ytDlpService, ffmpegService, historyStore, settingsStore);

        _viewModel = new AppViewModel(
            queueService,
            historyStore,
            settingsStore,
            new AvaloniaFileDialogService(this),
            PlatformServices.CreateClipboardMonitor(),
            pathService,
            metadataCache,
            toolResolver,
            updateService);

        DataContext = _viewModel;
        _ = _viewModel.InitializeAsync();
    }

    private void ApplyWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Clip.ico");
            if (File.Exists(iconPath))
            {
                Icon = new WindowIcon(iconPath);
            }
        }
        catch
        {
            // A missing or invalid icon must not block app startup.
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void OpenLogsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_viewModel.LogsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _viewModel.LogsDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"Unable to open logs folder: {ex.Message}";
        }
    }
}
