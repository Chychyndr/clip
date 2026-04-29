using System.Text.Json.Serialization;

namespace Clip.Core.Models;

public sealed class DownloadItem : ObservableEntity
{
    private string _title = "Queued download";
    private string _url = "";
    private Platform _platform = Platform.Unknown;
    private DownloadStatus _status = DownloadStatus.Pending;
    private double _progress;
    private string _statusText = "Queued";
    private string? _speed;
    private string? _eta;
    private string _currentStage = "Pending";
    private string? _thumbnail;
    private double? _durationSeconds;
    private string _mediaMode = "Video + audio";
    private string _format = "MP4";
    private string _resolution = "1080p";
    private string _saveDirectory = "";
    private string? _outputFilePath;
    private string? _errorMessage;
    private VideoMetadata? _metadata;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
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

    public VideoMetadata? Metadata
    {
        get => _metadata;
        set => SetProperty(ref _metadata, value);
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
        set
        {
            if (SetProperty(ref _progress, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(ProgressLabel));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string? Speed
    {
        get => _speed;
        set => SetProperty(ref _speed, value);
    }

    public string? Eta
    {
        get => _eta;
        set => SetProperty(ref _eta, value);
    }

    public string CurrentStage
    {
        get => _currentStage;
        set => SetProperty(ref _currentStage, string.IsNullOrWhiteSpace(value) ? "Working" : value);
    }

    public string? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    public double? DurationSeconds
    {
        get => _durationSeconds;
        set => SetProperty(ref _durationSeconds, value);
    }

    public string MediaMode
    {
        get => _mediaMode;
        set => SetProperty(ref _mediaMode, string.IsNullOrWhiteSpace(value) ? "Video + audio" : value);
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

    public bool IsActive => Status is DownloadStatus.Analyzing or DownloadStatus.Downloading or DownloadStatus.PostProcessing;
    public bool IsTerminal => Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Cancelled;
    public bool CanRetry => Status is DownloadStatus.Failed or DownloadStatus.Cancelled;
    public string ProgressLabel => $"{Progress:0}%";
}
