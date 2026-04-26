namespace Clip.Models;

public enum DownloadStatus
{
    Queued,
    Analyzing,
    Downloading,
    Converting,
    Compressing,
    Completed,
    Failed,
    Cancelled
}
