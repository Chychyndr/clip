using System.Collections.ObjectModel;
using Clip.Core.App;
using Clip.Core.History;
using Clip.Core.Models;
using Clip.Core.Queue;
using Clip.Core.Services;
using Clip.Core.Tools;
using Clip.Core.YtDlp;

namespace Clip.Core.DownloadQueue;

public sealed class DownloadQueueService
{
    private readonly YtDlpService _ytDlpService;
    private readonly FFmpegService _ffmpegService;
    private readonly DownloadHistoryStore _historyStore;
    private readonly IAppSettingsProvider _settingsProvider;
    private QueueService _queueService;

    public DownloadQueueService(
        YtDlpService ytDlpService,
        FFmpegService ffmpegService,
        DownloadHistoryStore historyStore,
        IAppSettingsProvider settingsProvider)
    {
        _ytDlpService = ytDlpService;
        _ffmpegService = ffmpegService;
        _historyStore = historyStore;
        _settingsProvider = settingsProvider;
        _queueService = CreateQueueService(settingsProvider.Current);
    }

    public ObservableCollection<DownloadItem> Items { get; } = [];

    public DownloadItem Enqueue(
        string url,
        string saveDirectory,
        string mediaMode,
        string format,
        string resolution,
        string? browserCookieSource = null)
    {
        var item = new DownloadItem
        {
            Url = url,
            SaveDirectory = saveDirectory,
            MediaMode = mediaMode,
            Format = format,
            Resolution = resolution,
            BrowserCookieSource = browserCookieSource,
            Platform = UrlDetector.DetectPlatform(url),
            Cancellation = new CancellationTokenSource()
        };
        Items.Add(item);
        return item;
    }

    public async Task AnalyzeAsync(DownloadItem item, CancellationToken cancellationToken = default)
    {
        item.Status = DownloadStatus.Analyzing;
        item.CurrentStage = "Analyzing";
        item.ErrorMessage = null;

        await _queueService.AnalyzeAsync(async token =>
        {
            using var linked = CreateLinkedToken(item, token, cancellationToken);
            var result = await _ytDlpService.AnalyzeAsync(item.Url, item.BrowserCookieSource, linked.Token);
            item.Metadata = result.Metadata;
            item.Title = result.Metadata.DisplayTitle;
            item.Thumbnail = result.Metadata.BestThumbnail;
            item.DurationSeconds = result.Metadata.DurationSeconds;
            item.ClipRange.DurationSeconds = result.Metadata.DurationSeconds ?? 0;
            item.Status = DownloadStatus.Ready;
            item.CurrentStage = result.IsFromCache ? "Ready from metadata cache" : "Ready";
        }, cancellationToken);
    }

    public async Task DownloadAsync(DownloadItem item, CancellationToken cancellationToken = default)
    {
        item.Status = DownloadStatus.Downloading;
        item.CurrentStage = "Downloading";
        item.Progress = 0;
        item.ErrorMessage = null;

        try
        {
            await _queueService.DownloadAsync(async downloadToken =>
            {
                using var linked = CreateLinkedToken(item, downloadToken, cancellationToken);
                var result = await _ytDlpService.DownloadAsync(
                    new YtDlpDownloadOptions
                    {
                        Url = item.Url,
                        SaveDirectory = item.SaveDirectory,
                        MediaMode = item.MediaMode,
                        Format = item.Format,
                        Resolution = item.Resolution,
                        BrowserCookieSource = item.BrowserCookieSource
                    },
                    progress =>
                    {
                        if (progress.Percent is { } percent)
                        {
                            item.Progress = percent;
                        }

                        item.Speed = progress.Speed;
                        item.Eta = progress.Eta;
                        item.CurrentStage = progress.Status ?? progress.Stage;
                    },
                    linked.Token);

                item.OutputFilePath = string.IsNullOrWhiteSpace(result.OutputPath) ? item.OutputFilePath : result.OutputPath;

                if (item.ClipRange.IsEnabled && item.OutputFilePath is not null)
                {
                    item.Status = DownloadStatus.PostProcessing;
                    item.CurrentStage = "Trimming";
                    await _queueService.RunFfmpegAsync(
                        ffmpegToken => _ffmpegService.TrimAsync(
                            item.OutputFilePath,
                            BuildTrimOutputPath(item.OutputFilePath),
                            item.ClipRange.StartSeconds,
                            item.ClipRange.EndSeconds,
                            ffmpegToken),
                        linked.Token);
                }

                item.Progress = 100;
                item.CompletedAt = DateTimeOffset.Now;
                item.Status = DownloadStatus.Completed;
                item.CurrentStage = "Completed";
                await _historyStore.AddAsync(ToHistoryEntry(item), linked.Token);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Cancelled;
            item.CurrentStage = "Cancelled";
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Failed;
            item.CurrentStage = "Failed";
            item.ErrorMessage = ex.Message;
        }
    }

    public void Cancel(DownloadItem item)
    {
        item.Cancellation?.Cancel();
        item.Status = DownloadStatus.Cancelled;
        item.CurrentStage = "Cancelled";
    }

    public void ApplySettings()
    {
        _settingsProvider.Current.Normalize();
        _queueService = CreateQueueService(_settingsProvider.Current);
    }

    private static QueueService CreateQueueService(AppSettings settings) =>
        new(settings.MaxConcurrentDownloads, settings.MaxConcurrentMetadataAnalysis, settings.MaxConcurrentFfmpegJobs);

    private static CancellationTokenSource CreateLinkedToken(
        DownloadItem item,
        CancellationToken queueToken,
        CancellationToken externalToken)
    {
        item.Cancellation ??= new CancellationTokenSource();
        return CancellationTokenSource.CreateLinkedTokenSource(item.Cancellation.Token, queueToken, externalToken);
    }

    private static string BuildTrimOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);
        return Path.Combine(directory, $"{fileName}.clip{extension}");
    }

    private static DownloadHistoryEntry ToHistoryEntry(DownloadItem item) =>
        new(
            item.Title,
            item.Url,
            item.Platform,
            item.Format,
            item.Resolution,
            item.OutputFilePath ?? "",
            item.CompletedAt ?? DateTimeOffset.Now,
            item.Status);

}
