using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Clip.Core.Cache;
using Clip.Core.App;
using Clip.Core.Files;
using Clip.Core.Ffmpeg;
using Clip.Core.Queue;
using Clip.Core.Services;
using Clip.Core.Tools;
using Clip.Core.YtDlp;

var tests = new (string Name, Func<Task> Test)[]
{
    ("MetadataCache saves, reads, expires, and invalidates by version", TestMetadataCacheAsync),
    ("YtDlpProgressParser parses stable progress and ignores unknown lines", TestProgressParser),
    ("FilenameSanitizer cleans invalid names and preserves extensions", TestFilenameSanitizer),
    ("QueueService respects download and ffmpeg concurrency and cancellation", TestQueueServiceAsync),
    ("ToolResolver selects platform binaries and requires opt-in for PATH", TestToolResolver),
    ("YtDlpReleaseSecurity verifies trusted release checksums", TestYtDlpReleaseSecurityAsync),
    ("YtDlpCommandBuilder uses explicit aria2c path and cookies", TestYtDlpCommandBuilder),
    ("FfmpegCommandBuilder uses stream copy and seconds for fast trim", TestFfmpegFastTrim),
    ("UrlDetector matches real domains only", TestUrlDetector),
    ("AppSettings normalizes concurrency and cookies", TestAppSettingsNormalize)
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

    var stalePath = Path.Combine(directory, "stale.json");
    await File.WriteAllTextAsync(stalePath, "{}");
    File.SetLastWriteTimeUtc(stalePath, clock.GetUtcNow().UtcDateTime.AddDays(-2));
    var largePath = Path.Combine(directory, "large.json");
    await File.WriteAllTextAsync(largePath, new string('x', 128));
    await cache.PruneAsync(TimeSpan.FromHours(24), maxFileBytes: 64);
    Assert(!File.Exists(stalePath), "Expected stale metadata cache file to be pruned.");
    Assert(!File.Exists(largePath), "Expected oversized metadata cache file to be pruned.");
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

    var parallelFfmpegQueue = new QueueService(maxConcurrentDownloads: 1, maxConcurrentAnalysis: 1, maxConcurrentFfmpegJobs: 2);
    currentFfmpeg = 0;
    maxFfmpeg = 0;
    var parallelFfmpegJobs = Enumerable.Range(0, 4).Select(_ => parallelFfmpegQueue.RunFfmpegAsync(async token =>
    {
        var current = Interlocked.Increment(ref currentFfmpeg);
        maxFfmpeg = Math.Max(maxFfmpeg, current);
        await Task.Delay(20, token);
        Interlocked.Decrement(ref currentFfmpeg);
    }, CancellationToken.None));
    await Task.WhenAll(parallelFfmpegJobs);
    Assert(maxFfmpeg == 2, "ffmpeg limit should use the configured value.");

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

    var pathDirectory = CreateTempDirectory();
    var pathTool = Path.Combine(pathDirectory, "ffprobe.exe");
    File.WriteAllText(pathTool, "");
    var disabledPathResolver = new ToolResolver(CreateTempDirectory(), new HostPlatform(HostOperatingSystem.Windows, HostArchitecture.X64), pathDirectory);
    var disabledPathResolved = disabledPathResolver.Resolve(ExternalTool.Ffprobe, ensureExecutable: false);
    Assert(!disabledPathResolved.IsFound, "PATH fallback should be disabled by default.");

    var appDirectory = CreateTempDirectory();
    File.WriteAllText(Path.Combine(appDirectory, "ffmpeg.exe"), "");
    var appDirectoryResolver = new ToolResolver(appDirectory, new HostPlatform(HostOperatingSystem.Windows, HostArchitecture.X64), "");
    var appDirectoryResolved = appDirectoryResolver.Resolve(ExternalTool.Ffmpeg, ensureExecutable: false);
    Assert(!appDirectoryResolved.IsFound, "Executables next to the app should not bypass bundled tool folders.");

    var pathResolver = new ToolResolver(
        CreateTempDirectory(),
        new HostPlatform(HostOperatingSystem.Windows, HostArchitecture.X64),
        pathDirectory,
        allowPathFallback: true);
    var pathResolved = pathResolver.Resolve(ExternalTool.Ffprobe, ensureExecutable: false);
    Assert(pathResolved.IsFound && pathResolved.IsFromPath && pathResolved.Path == pathTool, "Expected PATH fallback.");
    return Task.CompletedTask;
}

static async Task TestYtDlpReleaseSecurityAsync()
{
    const string assetUrl = "https://github.com/yt-dlp/yt-dlp/releases/download/2026.01.01/yt-dlp.exe";
    const string checksumUrl = "https://github.com/yt-dlp/yt-dlp/releases/download/2026.01.01/SHA2-256SUMS";
    var assetBytes = Encoding.ASCII.GetBytes("fake executable");
    var sha256 = Convert.ToHexString(SHA256.HashData(assetBytes)).ToLowerInvariant();
    using var client = new HttpClient(new StaticHttpHandler(new Dictionary<string, byte[]>
    {
        [assetUrl] = assetBytes,
        [checksumUrl] = Encoding.ASCII.GetBytes($"{sha256}  yt-dlp.exe{Environment.NewLine}")
    }));

    var verified = await YtDlpReleaseSecurity.DownloadVerifiedAssetAsync(
        client,
        "yt-dlp.exe",
        assetUrl,
        checksumUrl,
        CancellationToken.None);
    Assert(verified.SequenceEqual(assetBytes), "Expected verified release bytes.");

    await AssertThrowsSecurityAsync(() => YtDlpReleaseSecurity.DownloadVerifiedAssetAsync(
        client,
        "yt-dlp.exe",
        "https://example.com/yt-dlp.exe",
        checksumUrl,
        CancellationToken.None));

    using var mismatchClient = new HttpClient(new StaticHttpHandler(new Dictionary<string, byte[]>
    {
        [assetUrl] = assetBytes,
        [checksumUrl] = Encoding.ASCII.GetBytes($"{new string('0', 64)}  yt-dlp.exe{Environment.NewLine}")
    }));
    await AssertThrowsSecurityAsync(() => YtDlpReleaseSecurity.DownloadVerifiedAssetAsync(
        mismatchClient,
        "yt-dlp.exe",
        assetUrl,
        checksumUrl,
        CancellationToken.None));
}

static Task TestYtDlpCommandBuilder()
{
    var args = YtDlpCommandBuilder.BuildDownload(new YtDlpDownloadOptions
    {
        Url = "https://example.com/video",
        SaveDirectory = "C:\\Downloads With Spaces",
        MediaMode = "Video + audio",
        Format = "MP4",
        Resolution = "1080p",
        ConcurrentFragments = 4,
        UseAria2c = true,
        Aria2cPath = "C:\\Tools\\aria2c.exe",
        BrowserCookieSource = "chrome"
    }).ToArray();

    Assert(args.Contains("--concurrent-fragments"), "Expected yt-dlp concurrent fragments option.");
    Assert(args.Contains("4"), "Expected fragment count.");
    Assert(HasAdjacent(args, "--downloader", "C:\\Tools\\aria2c.exe"), "Expected bundled aria2c path to be passed.");
    Assert(HasAdjacent(args, "--cookies-from-browser", "chrome"), "Expected browser cookie source.");

    var batchArgs = YtDlpCommandBuilder.BuildBatchDownload(new YtDlpBatchDownloadOptions
    {
        BatchFilePath = "links.txt",
        SaveDirectory = "C:\\Downloads",
        BrowserCookieSource = "edge"
    }).ToArray();
    Assert(HasAdjacent(batchArgs, "--cookies-from-browser", "edge"), "Expected batch downloads to keep browser cookies.");
    return Task.CompletedTask;
}

static Task TestFfmpegFastTrim()
{
    var args = FfmpegCommandBuilder.BuildFastTrim(new FfmpegTrimOptions
    {
        InputPath = "input file.mp4",
        OutputPath = "output file.mp4",
        StartSeconds = 90000.5,
        EndSeconds = 90003.25
    }).ToArray();

    Assert(HasAdjacent(args, "-c", "copy"), "Fast trim should use stream copy.");
    Assert(!args.Contains("-preset"), "Fast trim should not re-encode.");
    Assert(HasAdjacent(args, "-ss", "90000.5"), "Long-video trim start should be passed as seconds.");
    return Task.CompletedTask;
}

static Task TestUrlDetector()
{
    Assert(UrlDetector.DetectPlatform("https://youtube.com/watch?v=1") == Clip.Core.Models.Platform.YouTube, "Expected youtube.com.");
    Assert(UrlDetector.DetectPlatform("https://m.youtube.com/watch?v=1") == Clip.Core.Models.Platform.YouTube, "Expected youtube subdomain.");
    Assert(UrlDetector.DetectPlatform("https://youtube.com.evil.example/watch?v=1") == Clip.Core.Models.Platform.Unknown, "Expected fake YouTube host to be unknown.");
    Assert(UrlDetector.DetectPlatform("https://notinstagram.com/reel/1") == Clip.Core.Models.Platform.Unknown, "Expected fake Instagram host to be unknown.");
    return Task.CompletedTask;
}

static Task TestAppSettingsNormalize()
{
    var settings = new AppSettings
    {
        MaxConcurrentDownloads = 99,
        MaxConcurrentMetadataAnalysis = 99,
        MaxConcurrentFfmpegJobs = 3,
        YtDlpConcurrentFragments = 99,
        BrowserCookieSource = " None "
    };

    settings.Normalize();

    Assert(settings.MaxConcurrentDownloads == 2, "Invalid download concurrency should reset.");
    Assert(settings.MaxConcurrentMetadataAnalysis == 3, "Invalid metadata concurrency should reset.");
    Assert(settings.MaxConcurrentFfmpegJobs == 1, "ffmpeg concurrency should remain serialized.");
    Assert(settings.YtDlpConcurrentFragments == 4, "Invalid fragment count should reset.");
    Assert(settings.BrowserCookieSource is null, "None cookie source should normalize to null.");
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

static bool HasAdjacent(IReadOnlyList<string> values, string left, string right)
{
    for (var index = 0; index < values.Count - 1; index++)
    {
        if (values[index] == left && values[index + 1] == right)
        {
            return true;
        }
    }

    return false;
}

static async Task AssertThrowsSecurityAsync(Func<Task> action)
{
    try
    {
        await action();
    }
    catch (SecurityException)
    {
        return;
    }

    throw new InvalidOperationException("Expected a security exception.");
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

sealed class StaticHttpHandler : HttpMessageHandler
{
    private readonly IReadOnlyDictionary<string, byte[]> _responses;

    public StaticHttpHandler(IReadOnlyDictionary<string, byte[]> responses)
    {
        _responses = responses;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null &&
            _responses.TryGetValue(request.RequestUri.AbsoluteUri, out var bytes))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
