namespace Clip.Core.Models;

public sealed record DownloadHistoryEntry(
    string Title,
    string Url,
    Platform Platform,
    string Format,
    string Resolution,
    string FilePath,
    DateTimeOffset CompletedAt,
    DownloadStatus Status = DownloadStatus.Completed);
