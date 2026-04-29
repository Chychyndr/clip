using System.Text.Json;
using Clip.Core.App;

namespace Clip.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IAppSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private bool _monitorClipboard = true;
    private bool _autoAnalyzeClipboard;
    private bool _hideToTrayOnClose = true;
    private bool _startMinimized;
    private bool _checkForYtDlpUpdates = true;
    private bool _keepOriginalWhenClipping;
    private int _maxConcurrentDownloads = 2;
    private int _maxConcurrentMetadataAnalysis = 3;
    private int _ytDlpConcurrentFragments = 4;
    private bool _useAria2c;
    private bool _fastBatchTextImport;
    private TrimMode _trimMode = TrimMode.Fast;
    private CompressionMode _compressionMode = CompressionMode.Balance;
    private VideoEncoderChoice _videoEncoder = VideoEncoderChoice.Auto;
    private int _metadataCacheTtlHours = 24;
    private bool _suppressSave;

    public IReadOnlyList<int> DownloadConcurrencyOptions { get; } = [1, 2, 3];
    public IReadOnlyList<int> AnalysisConcurrencyOptions { get; } = [2, 3, 4];
    public IReadOnlyList<int> FragmentOptions { get; } = [1, 4, 8];
    public IReadOnlyList<TrimMode> TrimModeOptions { get; } = [TrimMode.Fast, TrimMode.Exact];
    public IReadOnlyList<CompressionMode> CompressionModeOptions { get; } = [CompressionMode.Fast, CompressionMode.Balance, CompressionMode.Quality];
    public IReadOnlyList<VideoEncoderChoice> VideoEncoderOptions { get; } =
    [
        VideoEncoderChoice.Auto,
        VideoEncoderChoice.SoftwareX264,
        VideoEncoderChoice.SoftwareX265,
        VideoEncoderChoice.NvidiaH264,
        VideoEncoderChoice.NvidiaHevc,
        VideoEncoderChoice.IntelH264,
        VideoEncoderChoice.IntelHevc,
        VideoEncoderChoice.AmdH264,
        VideoEncoderChoice.AmdHevc,
        VideoEncoderChoice.AppleH264,
        VideoEncoderChoice.AppleHevc
    ];

    public AppSettings Current => CreateSnapshot();

    public bool MonitorClipboard
    {
        get => _monitorClipboard;
        set
        {
            if (SetProperty(ref _monitorClipboard, value))
            {
                Save();
            }
        }
    }

    public bool AutoAnalyzeClipboard
    {
        get => _autoAnalyzeClipboard;
        set
        {
            if (SetProperty(ref _autoAnalyzeClipboard, value))
            {
                Save();
            }
        }
    }

    public bool HideToTrayOnClose
    {
        get => _hideToTrayOnClose;
        set
        {
            if (SetProperty(ref _hideToTrayOnClose, value))
            {
                Save();
            }
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (SetProperty(ref _startMinimized, value))
            {
                Save();
            }
        }
    }

    public bool CheckForYtDlpUpdates
    {
        get => _checkForYtDlpUpdates;
        set
        {
            if (SetProperty(ref _checkForYtDlpUpdates, value))
            {
                Save();
            }
        }
    }

    public bool KeepOriginalWhenClipping
    {
        get => _keepOriginalWhenClipping;
        set
        {
            if (SetProperty(ref _keepOriginalWhenClipping, value))
            {
                Save();
            }
        }
    }

    public int MaxConcurrentDownloads
    {
        get => _maxConcurrentDownloads;
        set
        {
            if (SetProperty(ref _maxConcurrentDownloads, DownloadConcurrencyOptions.Contains(value) ? value : 2))
            {
                Save();
            }
        }
    }

    public int MaxConcurrentMetadataAnalysis
    {
        get => _maxConcurrentMetadataAnalysis;
        set
        {
            if (SetProperty(ref _maxConcurrentMetadataAnalysis, AnalysisConcurrencyOptions.Contains(value) ? value : 3))
            {
                Save();
            }
        }
    }

    public int MaxConcurrentFfmpegJobs => 1;

    public int YtDlpConcurrentFragments
    {
        get => _ytDlpConcurrentFragments;
        set
        {
            if (SetProperty(ref _ytDlpConcurrentFragments, FragmentOptions.Contains(value) ? value : 4))
            {
                Save();
            }
        }
    }

    public bool UseAria2c
    {
        get => _useAria2c;
        set
        {
            if (SetProperty(ref _useAria2c, value))
            {
                Save();
            }
        }
    }

    public bool FastBatchTextImport
    {
        get => _fastBatchTextImport;
        set
        {
            if (SetProperty(ref _fastBatchTextImport, value))
            {
                Save();
            }
        }
    }

    public TrimMode TrimMode
    {
        get => _trimMode;
        set
        {
            if (SetProperty(ref _trimMode, value))
            {
                Save();
            }
        }
    }

    public CompressionMode CompressionMode
    {
        get => _compressionMode;
        set
        {
            if (SetProperty(ref _compressionMode, value))
            {
                Save();
            }
        }
    }

    public VideoEncoderChoice VideoEncoder
    {
        get => _videoEncoder;
        set
        {
            if (SetProperty(ref _videoEncoder, value))
            {
                Save();
            }
        }
    }

    public int MetadataCacheTtlHours
    {
        get => _metadataCacheTtlHours;
        set
        {
            if (SetProperty(ref _metadataCacheTtlHours, Math.Clamp(value, 1, 24 * 30)))
            {
                Save();
            }
        }
    }

    public static SettingsViewModel Load()
    {
        var settings = new SettingsViewModel { _suppressSave = true };
        try
        {
            if (File.Exists(ClipConstants.SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(ClipConstants.SettingsPath),
                    JsonOptions);

                if (loaded is not null)
                {
                    loaded.Normalize();
                    settings.MonitorClipboard = loaded.MonitorClipboard;
                    settings.AutoAnalyzeClipboard = loaded.AutoAnalyzeClipboard;
                    settings.HideToTrayOnClose = loaded.HideToTrayOnClose;
                    settings.StartMinimized = loaded.StartMinimized;
                    settings.CheckForYtDlpUpdates = loaded.CheckForYtDlpUpdates;
                    settings.KeepOriginalWhenClipping = loaded.KeepOriginalWhenClipping;
                    settings.MaxConcurrentDownloads = loaded.MaxConcurrentDownloads;
                    settings.MaxConcurrentMetadataAnalysis = loaded.MaxConcurrentMetadataAnalysis;
                    settings.YtDlpConcurrentFragments = loaded.YtDlpConcurrentFragments;
                    settings.UseAria2c = loaded.UseAria2c;
                    settings.FastBatchTextImport = loaded.FastBatchTextImport;
                    settings.TrimMode = loaded.TrimMode;
                    settings.CompressionMode = loaded.CompressionMode;
                    settings.VideoEncoder = loaded.VideoEncoder;
                    settings.MetadataCacheTtlHours = loaded.MetadataCacheTtlHours;
                }
            }
        }
        catch
        {
            // Defaults are safe and keep the app usable.
        }
        finally
        {
            settings._suppressSave = false;
        }

        return settings;
    }

    private void Save()
    {
        if (_suppressSave)
        {
            return;
        }

        Directory.CreateDirectory(ClipConstants.AppDataDirectory);
        var snapshot = CreateSnapshot();
        File.WriteAllText(ClipConstants.SettingsPath, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    private AppSettings CreateSnapshot()
    {
        var snapshot = new AppSettings
        {
            MonitorClipboard = MonitorClipboard,
            AutoAnalyzeClipboard = AutoAnalyzeClipboard,
            HideToTrayOnClose = HideToTrayOnClose,
            StartMinimized = StartMinimized,
            CheckForYtDlpUpdates = CheckForYtDlpUpdates,
            KeepOriginalWhenClipping = KeepOriginalWhenClipping,
            MaxConcurrentDownloads = MaxConcurrentDownloads,
            MaxConcurrentMetadataAnalysis = MaxConcurrentMetadataAnalysis,
            MaxConcurrentFfmpegJobs = MaxConcurrentFfmpegJobs,
            YtDlpConcurrentFragments = YtDlpConcurrentFragments,
            UseAria2c = UseAria2c,
            FastBatchTextImport = FastBatchTextImport,
            TrimMode = TrimMode,
            CompressionMode = CompressionMode,
            VideoEncoder = VideoEncoder,
            MetadataCacheTtlHours = MetadataCacheTtlHours
        };

        snapshot.Normalize();
        return snapshot;
    }
}
