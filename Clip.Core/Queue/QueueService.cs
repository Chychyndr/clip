namespace Clip.Core.Queue;

public sealed class QueueService
{
    private readonly SemaphoreSlim _downloadLimiter;
    private readonly SemaphoreSlim _analysisLimiter;
    private readonly SemaphoreSlim _ffmpegLimiter;

    public QueueService(int maxConcurrentDownloads, int maxConcurrentAnalysis, int maxConcurrentFfmpegJobs = 1)
    {
        _downloadLimiter = new SemaphoreSlim(Math.Max(1, maxConcurrentDownloads), Math.Max(1, maxConcurrentDownloads));
        _analysisLimiter = new SemaphoreSlim(Math.Max(1, maxConcurrentAnalysis), Math.Max(1, maxConcurrentAnalysis));
        _ffmpegLimiter = new SemaphoreSlim(1, 1);
    }

    public async Task AnalyzeAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await RunLimitedAsync(_analysisLimiter, action, cancellationToken);
    }

    public async Task DownloadAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await RunLimitedAsync(_downloadLimiter, action, cancellationToken);
    }

    public async Task RunFfmpegAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await RunLimitedAsync(_ffmpegLimiter, action, cancellationToken);
    }

    public static async Task RunCancellableJobAsync(
        QueueJob job,
        Func<QueueJob, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, job.Cancellation.Token);
        try
        {
            await action(job, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            job.State = QueueJobState.Cancelled;
        }
    }

    private static async Task RunLimitedAsync(
        SemaphoreSlim semaphore,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await action(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }
}

public sealed class QueueJob
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public QueueJobState State { get; set; } = QueueJobState.Pending;
    public CancellationTokenSource Cancellation { get; } = new();

    public void Cancel() => Cancellation.Cancel();
}
