using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Clip.Models;
using Clip.Services;

namespace Clip.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly YTDLPService _ytDlpService;
    private readonly FileDialogService _fileDialogService;
    private readonly OutputPathHolder _outputPathHolder;
    private readonly UpdateService _updateService;
    private readonly CancellationTokenSource _shutdown = new();
    private string _urlText = "";
    private string _feedback = "Paste a link to begin.";
    private bool _isAnalyzing;
    private VideoMetadata? _metadata;
    private ImageSource? _thumbnailImage;
    private Platform _detectedPlatform = Platform.Unknown;
    private string _selectedFormat = "MP4";
    private string _selectedResolution = "1080p";
    private bool _useCustomTargetSize;
    private double _targetSizeMegabytes = 50;
    private string _saveDirectory = ClipConstants.DefaultDownloadDirectory;
    private bool _showSettings;
    private string? _updateMessage;

    public MainViewModel(
        YTDLPService ytDlpService,
        FileDialogService fileDialogService,
        OutputPathHolder outputPathHolder,
        UpdateService updateService,
        DownloadViewModel downloads,
        SettingsViewModel settings)
    {
        _ytDlpService = ytDlpService;
        _fileDialogService = fileDialogService;
        _outputPathHolder = outputPathHolder;
        _updateService = updateService;
        Downloads = downloads;
        Settings = settings;

        AnalyzeCommand = new AsyncRelayCommand(AnalyzeCurrentUrlAsync, () => !IsAnalyzing);
        PasteCommand = new AsyncRelayCommand(PasteFromClipboardAsync);
        QueueDownloadCommand = new AsyncRelayCommand(QueueCurrentDownloadAsync, () => !IsAnalyzing);
        ChooseFolderCommand = new AsyncRelayCommand(ChooseFolderAsync);
        ClearCommand = new RelayCommand(Clear);
        DismissUpdateCommand = new RelayCommand(() => UpdateMessage = null);
        ToggleSettingsCommand = new RelayCommand(() => ShowSettings = !ShowSettings);
    }

    public IReadOnlyList<string> Formats { get; } = ["MP4", "MOV", "WebM", "MP3"];
    public IReadOnlyList<string> Resolutions { get; } = ["4K", "1440p", "1080p", "720p", "480p", "360p", "Original"];

    public DownloadViewModel Downloads { get; }
    public SettingsViewModel Settings { get; }
    public ClipRange ClipRange { get; } = new();

    public AsyncRelayCommand AnalyzeCommand { get; }
    public AsyncRelayCommand PasteCommand { get; }
    public AsyncRelayCommand QueueDownloadCommand { get; }
    public AsyncRelayCommand ChooseFolderCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand DismissUpdateCommand { get; }
    public RelayCommand ToggleSettingsCommand { get; }

    public IntPtr WindowHandle { get; set; }

    public string UrlText
    {
        get => _urlText;
        set
        {
            if (!SetProperty(ref _urlText, value))
            {
                return;
            }

            DetectedPlatform = URLDetector.TryExtractFirstUrl(value, out var url)
                ? URLDetector.DetectPlatform(url)
                : Platform.Unknown;
            OnPropertyChanged(nameof(CanAnalyze));
        }
    }

    public string Feedback
    {
        get => _feedback;
        set => SetProperty(ref _feedback, value);
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        set
        {
            if (SetProperty(ref _isAnalyzing, value))
            {
                AnalyzeCommand.NotifyCanExecuteChanged();
                QueueDownloadCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public VideoMetadata? Metadata
    {
        get => _metadata;
        set
        {
            if (SetProperty(ref _metadata, value))
            {
                OnPropertyChanged(nameof(HasMetadata));
            }
        }
    }

    public bool HasMetadata => Metadata is not null;

    public ImageSource? ThumbnailImage
    {
        get => _thumbnailImage;
        set => SetProperty(ref _thumbnailImage, value);
    }

    public Platform DetectedPlatform
    {
        get => _detectedPlatform;
        set => SetProperty(ref _detectedPlatform, value);
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

    public bool UseCustomTargetSize
    {
        get => _useCustomTargetSize;
        set => SetProperty(ref _useCustomTargetSize, value);
    }

    public double TargetSizeMegabytes
    {
        get => _targetSizeMegabytes;
        set => SetProperty(ref _targetSizeMegabytes, Math.Max(1, value));
    }

    public string SaveDirectory
    {
        get => _saveDirectory;
        set
        {
            if (SetProperty(ref _saveDirectory, value))
            {
                _ = _outputPathHolder.SetAsync(value);
            }
        }
    }

    public bool ShowSettings
    {
        get => _showSettings;
        set => SetProperty(ref _showSettings, value);
    }

    public string? UpdateMessage
    {
        get => _updateMessage;
        set
        {
            if (SetProperty(ref _updateMessage, value))
            {
                OnPropertyChanged(nameof(HasUpdateMessage));
            }
        }
    }

    public bool HasUpdateMessage => !string.IsNullOrWhiteSpace(UpdateMessage);
    public bool CanAnalyze => URLDetector.IsSupportedUrl(UrlText);

    public async Task InitializeAsync()
    {
        await _outputPathHolder.SetAsync(SaveDirectory);
        var missing = _ytDlpService.GetMissingBinaries(includeFfmpeg: true);
        if (missing.Count > 0)
        {
            UpdateMessage = "Add yt-dlp.exe, ffmpeg.exe, and ffprobe.exe to Resources\\bin before downloading.";
            return;
        }

        if (Settings.CheckForYtDlpUpdates)
        {
            UpdateMessage = await _updateService.CheckForYtDlpUpdateAsync(_shutdown.Token);
        }
    }

    public async Task AnalyzeCurrentUrlAsync()
    {
        if (!URLDetector.TryExtractFirstUrl(UrlText, out var url))
        {
            Feedback = "That does not look like a video URL.";
            return;
        }

        UrlText = url;
        IsAnalyzing = true;
        Metadata = null;
        ThumbnailImage = null;
        Feedback = "Analyzing...";

        try
        {
            DetectedPlatform = URLDetector.DetectPlatform(url);
            var metadata = await _ytDlpService.AnalyzeAsync(url, _shutdown.Token);
            Metadata = metadata;
            ClipRange.DurationSeconds = metadata.DurationSeconds ?? 0;
            ClipRange.StartSeconds = 0;
            ClipRange.EndSeconds = ClipRange.DurationSeconds;
            SetThumbnail(metadata.BestThumbnail);
            Feedback = "Ready to download.";
        }
        catch (MissingBinaryException ex)
        {
            Feedback = "Missing required binary.";
            UpdateMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Feedback = ex.Message;
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    public async Task PasteFromClipboardAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            var text = content.Contains(StandardDataFormats.WebLink)
                ? (await content.GetWebLinkAsync())?.ToString()
                : content.Contains(StandardDataFormats.Text)
                    ? await content.GetTextAsync()
                    : "";

            if (URLDetector.TryExtractFirstUrl(text, out var url))
            {
                UrlText = url;
                Feedback = "Link pasted.";
            }
            else
            {
                Feedback = "Clipboard does not contain a supported URL.";
            }
        }
        catch (Exception ex)
        {
            Feedback = ex.Message;
        }
    }

    public async Task QueueCurrentDownloadAsync()
    {
        if (!URLDetector.TryExtractFirstUrl(UrlText, out var url))
        {
            Feedback = "Add a URL before queueing.";
            return;
        }

        if (Metadata is null)
        {
            await AnalyzeCurrentUrlAsync();
            if (Metadata is null)
            {
                return;
            }
        }

        var item = new DownloadItem
        {
            Url = url,
            Metadata = Metadata,
            Title = Metadata.DisplayTitle,
            Platform = DetectedPlatform,
            Format = SelectedFormat,
            Resolution = SelectedResolution,
            UseCustomTargetSize = UseCustomTargetSize,
            TargetSizeMegabytes = TargetSizeMegabytes,
            SaveDirectory = SaveDirectory
        };
        item.ClipRange.IsEnabled = ClipRange.IsEnabled;
        item.ClipRange.DurationSeconds = ClipRange.DurationSeconds;
        item.ClipRange.StartSeconds = ClipRange.StartSeconds;
        item.ClipRange.EndSeconds = ClipRange.EndSeconds;

        await Downloads.EnqueueAsync(item);
        Feedback = "Added to downloads.";
    }

    public async Task ChooseFolderAsync()
    {
        if (WindowHandle == IntPtr.Zero)
        {
            Feedback = "Window is not ready for folder selection.";
            return;
        }

        var folder = await _fileDialogService.PickFolderAsync(WindowHandle);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SaveDirectory = folder;
        }
    }

    public async Task AcceptDetectedClipboardUrlAsync(string url)
    {
        if (!Settings.MonitorClipboard)
        {
            return;
        }

        var currentHasUrl = URLDetector.IsSupportedUrl(UrlText);
        if (currentHasUrl && !string.Equals(UrlText, url, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        UrlText = url;
        Feedback = "Detected a URL on the clipboard.";
        if (Settings.AutoAnalyzeClipboard)
        {
            await AnalyzeCurrentUrlAsync();
        }
    }

    public async Task HandleCommandLineAsync(string command)
    {
        if (URLDetector.TryExtractFirstUrl(command, out var url))
        {
            UrlText = url;
            await AnalyzeCurrentUrlAsync();
        }
    }

    public void Clear()
    {
        UrlText = "";
        Metadata = null;
        ThumbnailImage = null;
        ClipRange.IsEnabled = false;
        ClipRange.DurationSeconds = 0;
        Feedback = "Paste a link to begin.";
    }

    public void Dispose() => _shutdown.Cancel();

    private void SetThumbnail(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            ThumbnailImage = null;
            return;
        }

        ThumbnailImage = new BitmapImage(uri);
    }
}
