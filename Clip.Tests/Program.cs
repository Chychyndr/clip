using Clip.Core.Cache;
using Clip.Core.Files;
using Clip.Core.Queue;
using Clip.Core.Tools;
using Clip.Core.YtDlp;

var tests = new (string Name, Func<Task> Test)[]
{
    ("MetadataCache saves, reads, expires, and invalidates by version", TestMetadataCacheAsync),
    ("YtDlpProgressParser parses stable progress and ignores unknown lines", TestProgressParser),
    ("FilenameSanitizer cleans invalid names and preserves extensions", TestFilenameSanitizer),
    ("QueueService respects download and ffmpeg concurrency and cancellation", TestQueueServiceAsync),
    ("ToolResolver selects platform binaries and falls back to PATH", TestToolResolver)
};

var failed = 0;
foreach (var (name, test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine(ex);
    }
}

if (failed > 0)
{
    Environment.ExitCode = 1;
}

static async Task TestMetadataCacheAsync()
{
    var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-04-29T00:00:00Z"));
    var directory = CreateTempDirectory();
    var cache = new MetadataCacheService(directory, clock);
    const string url = "https://Example.com/watch?v=1";
    const string json = "{\"title\":\"Clip\"}";

    await cache.SaveAsync(url, "2026.01.01", "default", json);
    var hit = await cache.TryReadAsync("https://example.com/watch?v=1", "2026.01.01", "default", TimeSpan.FromHours(24));
    Assert(hit.Hit, "Expected a fresh cache hit.");
    Assert(hit.MetadataJson == json, "Expected cached JSON to round-trip.");

    var wrongVersion = await cache.TryReadAsync(url, "2026.02.01", "default", TimeSpan.FromHours(24));
    Assert(!wrongVersion.Hit, "Cache should miss when yt-dlp version changes.");

    clock.Advance(TimeSpan.FromHours(25));
    var expired = await cache.TryReadAsync(url, "2026.01.01", "default", TimeSpan.FromHours(24));
    Assert(expired.IsExpired, "Cache should expire after TTL.");
}

static Task TestProgressParser()
{
    var parser = new YtDlpProgressParser();
    Assert(parser.TryParse("download: 42.5%|1.2MiB/s|00:10|downloading", out var progress), "Expected progress line to parse.");
    Assert(progress.Percent == 42.5, "Expected percent.");
    Assert(progress.Speed == "1.2MiB/s", "Expected speed.");
    Assert(progress.Eta == "00:10", "Expected ETA.");
    Assert(progress.Status == "downloading", "Expected status.");
    Assert(!parser.TryParse("unexpected yt-dlp output", out _), "Unknown lines should not parse.");
    return Task.CompletedTask;
}

static Task TestFilenameSanitizer()
{
    Assert(FilenameSanitizer.Sanitize("A/B:C*D?.mp4") == "A_B_C_D.mp4", "Expected invalid characters to become underscores.");
    Assert(FilenameSanitizer.Sanitize("????") == "download", "Expected fallback name.");
    var longName = new string('a', 300) + ".mp4";
    var sanitized = FilenameSanitizer.Sanitize(longName, maxFileNameLength: 64);
    Assert(sanitized.EndsWith(".mp4", StringComparison.Ordinal), "Expected extension to be preserved.");
    Assert(sanitized.Length <= 64, "Expected length limit.");
    return Task.CompletedTask;
}

static async Task TestQueueServiceAsync()
{
    var queue = new QueueService(maxConcurrentDownloads: 2, maxConcurrentAnalysis: 3);
    var currentDownloads = 0;
    var maxDownloads = 0;
    var downloads = Enumerable.Range(0, 8).Select(_ => queue.DownloadAsync(async token =>
    {
        var current = Interlocked.Increment(ref currentDownloads);
        maxDownloads = Math.Max(maxDownloads, current);
        await Task.Delay(40, token);
        Interlocked.Decrement(ref currentDownloads);
    }, CancellationToken.None));
    await Task.WhenAll(downloads);
    Assert(maxDownloads <= 2, "Download limit was exceeded.");
    Assert(maxDownloads == 2, "Download limit was not exercised.");

    var currentFfmpeg = 0;
    var maxFfmpeg = 0;
    var ffmpegJobs = Enumerable.Range(0, 5).Select(_ => queue.RunFfmpegAsync(async token =>
    {
        var current = Interlocked.Increment(ref currentFfmpeg);
        maxFfmpeg = Math.Max(maxFfmpeg, current);
        await Task.Delay(20, token);
        Interlocked.Decrement(ref currentFfmpeg);
    }, CancellationToken.None));
    await Task.WhenAll(ffmpegJobs);
    Assert(maxFfmpeg == 1, "ffmpeg jobs should be serialized.");

    var job = new QueueJob();
    var running = QueueService.RunCancellableJobAsync(job, async (_, token) =>
    {
        await Task.Delay(TimeSpan.FromSeconds(5), token);
    }, CancellationToken.None);
    job.Cancel();
    await running;
    Assert(job.State == QueueJobState.Cancelled, "Cancellation should mark the job as cancelled.");
}

static Task TestToolResolver()
{
    var appBase = CreateTempDirectory();
    var winDirectory = Path.Combine(appBase, "Resources", "bin", "win-x64");
    Directory.CreateDirectory(winDirectory);
    var winTool = Path.Combine(winDirectory, "yt-dlp.exe");
    File.WriteAllText(winTool, "");

    var winResolver = new ToolResolver(appBase, new HostPlatform(HostOperatingSystem.Windows, HostArchitecture.X64), "");
    var winResolved = winResolver.Resolve(ExternalTool.YtDlp, ensureExecutable: false);
    Assert(winResolved.IsFound && winResolved.Path == winTool, "Expected win-x64 bundled yt-dlp.");

    var macBase = CreateTempDirectory();
    var macDirectory = Path.Combine(macBase, "Resources", "bin", "macos-arm64");
    Directory.CreateDirectory(macDirectory);
    var macTool = Path.Combine(macDirectory, "ffmpeg");
    File.WriteAllText(macTool, "");

    var macResolver = new ToolResolver(macBase, new HostPlatform(HostOperatingSystem.MacOS, HostArchitecture.Arm64), "");
    var macResolved = macResolver.Resolve(ExternalTool.Ffmpeg, ensureExecutable: false);
    Assert(macResolved.IsFound && macResolved.Path == macTool, "Expected macos-arm64 bundled ffmpeg.");

    var pathDirectory = CreateTempDirectory();
    var pathTool = Path.Combine(pathDirectory, "ffprobe.exe");
    File.WriteAllText(pathTool, "");
    var pathResolver = new ToolResolver(CreateTempDirectory(), new HostPlatform(HostOperatingSystem.Windows, HostArchitecture.X64), pathDirectory);
    var pathResolved = pathResolver.Resolve(ExternalTool.Ffprobe, ensureExecutable: false);
    Assert(pathResolved.IsFound && pathResolved.IsFromPath && pathResolved.Path == pathTool, "Expected PATH fallback.");
    return Task.CompletedTask;
}

static string CreateTempDirectory()
{
    var directory = Path.Combine(Path.GetTempPath(), "clip-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan value) => _now += value;
}
