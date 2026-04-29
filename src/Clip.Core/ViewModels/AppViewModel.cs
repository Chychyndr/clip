using System.Collections.ObjectModel;
using Clip.Core.App;
using Clip.Core.Cache;
using Clip.Core.DownloadQueue;
using Clip.Core.History;
using Clip.Core.Models;
using Clip.Core.Platform;
using Clip.Core.Services;
using Clip.Core.Settings;
using Clip.Core.Tools;

namespace Clip.Core.ViewModels;

public sealed class AppViewModel : ObservableEntity, IDisposable
{
    private readonly DownloadQueueService _queueService;
    private readonly DownloadHistoryStore _historyStore;
    private readonly SettingsStore _settingsStore;
    private readonly IFileDialogService _fileDialogService;
    private readonly IClipboardMonitor _clipboardMonitor;
    private readonly IAppPathService _pathService;
    private readonly MetadataCacheService _metadataCacheService;
    private readonly ToolResolver _toolResolver;
    private readonly YtDlpUpdateService _ytDlpUpdateService;
    private CancellationTokenSource? _autoAnalyzeDelay;
    private string _url = "";
    private string _statusMessage = "Ready";
    private string _selectedMediaMode = "Video + audio";
    private string _selectedFormat = "MP4";
    private string _selectedResolution = "1080p";
    private string _saveDirectory;
    private bool _isBusy;

    public AppViewModel(
        DownloadQueueService queueService,
        DownloadHistoryStore historyStore,
        SettingsStore settingsStore,
        IFileDialogService fileDialogService,
        IClipboardMonitor clipboardMonitor,
        IAppPathService pathService,
        MetadataCacheService metadataCacheService,
        ToolResolver toolResolver,
        YtDlpUpdateService ytDlpUpdateService)
    {
        _queueService = queueService;
        _historyStore = historyStore;
        _settingsStore = settingsStore;
        _fileDialogService = fileDialogService;
        _clipboardMonitor = clipboardMonitor;
        _pathService = pathService;
        _metadataCacheService = metadataCacheService;
        _toolResolver = toolResolver;
        _ytDlpUpdateService = ytDlpUpdateService;
        _saveDirectory = pathService.DefaultDownloadsDirectory;

        AnalyzeCommand = new AsyncRelayCommand(AnalyzeCurrentUrlAsync, () => UrlDetector.TryNormalize(Url, out _));
        AddToQueueCommand = new AsyncRelayCommand(AddCurrentUrlToQueueAsync, () => UrlDetector.TryNormalize(Url, out _));
        ImportTextFileCommand = new AsyncRelayCommand(ImportTextFileAsync);
        PickFolderCommand = new AsyncRelayCommand(PickFolderAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        ClearMetadataCacheCommand = new RelayCommand(ClearMetadataCache);
        CheckYtDlpCommand = new AsyncRelayCommand(() => CheckToolAsync(ExternalTool.YtDlp));
        CheckFfmpegCommand = new AsyncRelayCommand(() => CheckToolAsync(ExternalTool.Ffmpeg));
        UpdateYtDlpCommand = new AsyncRelayCommand(UpdateYtDlpAsync);
        CancelItemCommand = new RelayCommand(parameter =>
        {
            if (parameter is DownloadItem item)
            {
                _queueService.Cancel(item);
            }
        });

        _clipboardMonitor.SupportedUrlDetected += OnSupportedUrlDetected;
        _clipboardMonitor.IsEnabled = Settings.MonitorClipboard;
    }

    public AppSettings Settings => _settingsStore.Current;
    public ObservableCollection<DownloadItem> Downloads => _queueService.Items;
    public ObservableCollection<DownloadHistoryEntry> History => _historyStore.Items;

    public IReadOnlyList<string> MediaModes { get; } = ["Video + audio", "Only audio", "Only video"];
    public IReadOnlyList<string> Formats { get; } = ["MP4", "MOV", "WebM", "MP3", "Original", "Best"];
    public IReadOnlyList<string> Resolutions { get; } = ["Original", "4K", "2160p", "1440p", "1080p", "720p", "480p", "360p"];
    public IReadOnlyList<int> DownloadConcurrencyOptions { get; } = [1, 2, 3];
    public IReadOnlyList<int> AnalysisConcurrencyOptions { get; } = [2, 3, 4];
    public IReadOnlyList<int> FragmentOptions { get; } = [1, 4, 8];
    public IReadOnlyList<TrimMode> TrimModes { get; } = [TrimMode.Fast, TrimMode.Exact];
    public IReadOnlyList<CompressionMode> CompressionModes { get; } = [CompressionMode.Fast, CompressionMode.Balance, CompressionMode.Quality];
    public IReadOnlyList<VideoEncoderChoice> EncoderChoices { get; } = Enum.GetValues<VideoEncoderChoice>();

    public string Url
    {
        get => _url;
        set
        {
            if (!SetProperty(ref _url, value))
            {
                return;
            }

            AnalyzeCommand.RaiseCanExecuteChanged();
            AddToQueueCommand.RaiseCanExecuteChanged();
            ScheduleAutoAnalyze();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string SelectedMediaMode
    {
        get => _selectedMediaMode;
        set => SetProperty(ref _selectedMediaMode, value);
    }

    public string SelectedFormat
    {
        get => _selectedFormat;
        set => SetProperty(ref _selectedFormat, value);
    }

    public string SelectedResolution
    {
        get => _selectedResolution;
        set => SetProperty(ref _selectedResolution, value);
    }

    public string SaveDirectory
    {
        get => _saveDirectory;
        set => SetProperty(ref _saveDirectory, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public AsyncRelayCommand AnalyzeCommand { get; }
    public AsyncRelayCommand AddToQueueCommand { get; }
    public AsyncRelayCommand ImportTextFileCommand { get; }
    public AsyncRelayCommand PickFolderCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public RelayCommand ClearMetadataCacheCommand { get; }
    public AsyncRelayCommand CheckYtDlpCommand { get; }
    public AsyncRelayCommand CheckFfmpegCommand { get; }
    public AsyncRelayCommand UpdateYtDlpCommand { get; }
    public RelayCommand CancelItemCommand { get; }

    public string LogsDirectory => _pathService.LogsDirectory;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ClipPaths.EnsureUserDirectories();
        Directory.CreateDirectory(_pathService.DefaultDownloadsDirectory);
        await _historyStore.LoadAsync(cancellationToken);
        MarkInterruptedJobs();
    }

    private async Task AnalyzeCurrentUrlAsync()
    {
        if (!UrlDetector.TryNormalize(Url, out var normalized))
        {
            StatusMessage = "Enter a valid URL.";
            return;
        }

        IsBusy = true;
        try
        {
            var item = Downloads.FirstOrDefault(download => string.Equals(download.Url, normalized, StringComparison.OrdinalIgnoreCase))
                ?? _queueService.Enqueue(normalized, SaveDirectory, SelectedMediaMode, SelectedFormat, SelectedResolution);
            await _queueService.AnalyzeAsync(item);
            StatusMessage = item.Metadata?.IsFromCache == true ? "Metadata loaded from cache." : "Metadata analyzed.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddCurrentUrlToQueueAsync()
    {
        if (!UrlDetector.TryNormalize(Url, out var normalized))
        {
            StatusMessage = "Enter a valid URL.";
            return;
        }

        var item = Downloads.FirstOrDefault(download => string.Equals(download.Url, normalized, StringComparison.OrdinalIgnoreCase))
            ?? _queueService.Enqueue(normalized, SaveDirectory, SelectedMediaMode, SelectedFormat, SelectedResolution);
        _ = Task.Run(() => _queueService.DownloadAsync(item));
        StatusMessage = "Download queued.";
        await Task.CompletedTask;
    }

    private async Task ImportTextFileAsync()
    {
        var path = await _fileDialogService.PickTextFileAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(path);
            var urls = UrlDetector.ExtractDistinctUrls(lines);
            foreach (var url in urls)
            {
                _queueService.Enqueue(url, SaveDirectory, SelectedMediaMode, SelectedFormat, SelectedResolution);
            }

            StatusMessage = $"Imported {urls.Count} URL(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to read links file: {ex.Message}";
        }
    }

    private async Task PickFolderAsync()
    {
        var folder = await _fileDialogService.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SaveDirectory = folder;
        }
    }

    private async Task SaveSettingsAsync()
    {
        Settings.Normalize();
        await _settingsStore.SaveAsync();
        _queueService.ApplySettings();
        _clipboardMonitor.IsEnabled = Settings.MonitorClipboard;
        StatusMessage = "Settings saved.";
    }

    private void ClearMetadataCache()
    {
        _metadataCacheService.Clear();
        StatusMessage = "Metadata cache cleared.";
    }

    private async Task CheckToolAsync(ExternalTool tool)
    {
        await Task.Yield();
        var resolved = _toolResolver.Resolve(tool);
        StatusMessage = resolved.IsFound
            ? $"{resolved.DisplayName} found: {resolved.Path}"
            : resolved.Message ?? $"{resolved.DisplayName} was not found.";
    }

    private async Task UpdateYtDlpAsync()
    {
        var progress = new Progress<string>(message => StatusMessage = message);
        var result = await _ytDlpUpdateService.UpdateAsync(progress);
        StatusMessage = result.Message;
    }

    private void ScheduleAutoAnalyze()
    {
        if (!Settings.AutoAnalyzeClipboard || !UrlDetector.TryNormalize(Url, out _))
        {
            return;
        }

        _autoAnalyzeDelay?.Cancel();
        _autoAnalyzeDelay = new CancellationTokenSource();
        var token = _autoAnalyzeDelay.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(600, token);
                await AnalyzeCommand.ExecuteAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void OnSupportedUrlDetected(object? sender, string url)
    {
        if (Settings.AutoAnalyzeClipboard)
        {
            Url = url;
        }
    }

    private void MarkInterruptedJobs()
    {
        foreach (var item in Downloads.Where(item => item.IsActive))
        {
            item.Status = DownloadStatus.Failed;
            item.CurrentStage = "Interrupted";
            item.ErrorMessage = "The application closed before this task finished.";
        }
    }

    public void Dispose()
    {
        _autoAnalyzeDelay?.Cancel();
        _clipboardMonitor.SupportedUrlDetected -= OnSupportedUrlDetected;
        _clipboardMonitor.Dispose();
    }
}
