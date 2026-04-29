namespace Clip.Core.App;

public sealed class AppSettings
{
    public bool MonitorClipboard { get; set; } = true;
    public bool AutoAnalyzeClipboard { get; set; } = true;
    public bool HideToTrayOnClose { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool CheckForYtDlpUpdates { get; set; } = true;
    public bool KeepOriginalWhenClipping { get; set; }
    public int MaxConcurrentDownloads { get; set; } = 2;
    public int MaxConcurrentMetadataAnalysis { get; set; } = 3;
    public int MaxConcurrentFfmpegJobs { get; set; } = 1;
    public int YtDlpConcurrentFragments { get; set; } = 4;
    public bool UseAria2c { get; set; }
    public bool FastBatchTextImport { get; set; }
    public TrimMode TrimMode { get; set; } = TrimMode.Fast;
    public CompressionMode CompressionMode { get; set; } = CompressionMode.Balance;
    public VideoEncoderChoice VideoEncoder { get; set; } = VideoEncoderChoice.Auto;
    public int MetadataCacheTtlHours { get; set; } = 24;

    public void Normalize()
    {
        MaxConcurrentDownloads = ClampToAllowed(MaxConcurrentDownloads, [1, 2, 3], 2);
        MaxConcurrentMetadataAnalysis = ClampToAllowed(MaxConcurrentMetadataAnalysis, [2, 3, 4], 3);
        MaxConcurrentFfmpegJobs = 1;
        YtDlpConcurrentFragments = ClampToAllowed(YtDlpConcurrentFragments, [1, 4, 8], 4);
        MetadataCacheTtlHours = Math.Clamp(MetadataCacheTtlHours, 1, 24 * 30);
    }

    private static int ClampToAllowed(int value, IReadOnlyList<int> allowed, int fallback) =>
        allowed.Contains(value) ? value : fallback;
}

public interface IAppSettingsProvider
{
    AppSettings Current { get; }
}

public enum TrimMode
{
    Fast,
    Exact
}

public enum CompressionMode
{
    Fast,
    Balance,
    Quality
}

public enum VideoEncoderChoice
{
    Auto,
    SoftwareX264,
    SoftwareX265,
    NvidiaH264,
    NvidiaHevc,
    IntelH264,
    IntelHevc,
    AmdH264,
    AmdHevc,
    AppleH264,
    AppleHevc
}
