namespace Clip.Core.Queue;

public enum QueueJobState
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
