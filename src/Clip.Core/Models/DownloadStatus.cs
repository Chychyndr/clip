namespace Clip.Core.Models;

public enum DownloadStatus
{
    Pending,
    Analyzing,
    Ready,
    Downloading,
    PostProcessing,
    Completed,
    Failed,
    Cancelled
}
