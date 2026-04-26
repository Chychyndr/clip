using System.Text.Json.Serialization;

namespace Clip.Models;

public sealed class DownloadItem : ObservableEntity
{
    private string _title = "Queued download";
    private string _url = "";
    private Platform _platform = Platform.Unknown;
    private DownloadStatus _status = DownloadStatus.Queued;
    private double _progress;
    private string _statusText = "Queued";
    private string _format = "MP4";
    private string _resolution = "1080p";
    private bool _useCustomTargetSize;
    private bool _isCancelled;
    private double _targetSizeMegabytes = 50;
    private string _saveDirectory = ClipConstants.DefaultDownloadDirectory;
    private string? _outputFilePath;
    private string? _errorMessage;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public VideoMetadata? Metadata { get; init; }
    public ClipRange ClipRange { get; init; } = new();

    [JsonIgnore]
    public CancellationTokenSource? Cancellation { get; set; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, string.IsNullOrWhiteSpace(value) ? "Queued download" : value);
    }

    public string Url
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    public Platform Platform
    {
        get => _platform;
        set => SetProperty(ref _platform, value);
    }

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value))
            {
                return;
            }

            StatusText = value.ToString();
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(IsTerminal));
            OnPropertyChanged(nameof(CanRetry));
        }
    }

    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string Format
    {
        get => _format;
        set => SetProperty(ref _format, value);
    }

    public string Resolution
    {
        get => _resolution;
        set => SetProperty(ref _resolution, value);
    }

    public bool UseCustomTargetSize
    {
        get => _useCustomTargetSize;
        set => SetProperty(ref _useCustomTargetSize, value);
    }

    public bool IsCancelled
    {
        get => _isCancelled;
        set => SetProperty(ref _isCancelled, value);
    }

    public double TargetSizeMegabytes
    {
        get => _targetSizeMegabytes;
        set => SetProperty(ref _targetSizeMegabytes, Math.Max(1, value));
    }

    public string SaveDirectory
    {
        get => _saveDirectory;
        set => SetProperty(ref _saveDirectory, value);
    }

    public string? OutputFilePath
    {
        get => _outputFilePath;
        set => SetProperty(ref _outputFilePath, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public bool IsActive =>
        Status is DownloadStatus.Analyzing or DownloadStatus.Downloading or DownloadStatus.Converting or DownloadStatus.Compressing;

    public bool IsTerminal =>
        Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled;

    public bool CanRetry => Status is DownloadStatus.Failed or DownloadStatus.Cancelled;
}
