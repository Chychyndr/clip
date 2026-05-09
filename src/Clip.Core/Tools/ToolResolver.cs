namespace Clip.Core.Tools;

public sealed class ToolResolver
{
    private readonly string _appBaseDirectory;
    private readonly HostPlatform _platform;
    private readonly string _environmentPath;
    private readonly bool _allowPathFallback;

    public ToolResolver(
        string? appBaseDirectory = null,
        HostPlatform? platform = null,
        string? environmentPath = null,
        bool allowPathFallback = false)
    {
        _appBaseDirectory = appBaseDirectory ?? AppContext.BaseDirectory;
        _platform = platform ?? HostPlatformDetector.Detect();
        _environmentPath = environmentPath ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        _allowPathFallback = allowPathFallback;
    }

    public HostPlatform Platform => _platform;
    public bool AllowPathFallback => _allowPathFallback;

    public static string GetRuntimeFolder() => HostPlatformDetector.Detect().ResourceFolderName;

    public static string GetExecutableName(string toolName)
    {
        var platform = HostPlatformDetector.Detect();
        return platform.IsWindows && !toolName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? $"{toolName}.exe"
            : toolName;
    }

    public ExternalToolResolution Resolve(ExternalTool tool, bool ensureExecutable = true)
    {
        var displayName = GetDisplayName(tool);
        foreach (var candidate in GetBundledCandidates(tool))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            return ExternalToolResolution.Found(
                tool,
                displayName,
                candidate,
                isFromPath: false,
                message: null);
        }

        if (!_allowPathFallback)
        {
            return ExternalToolResolution.Missing(
                tool,
                displayName,
                $"{displayName} was not found in bundled tools. PATH fallback is disabled.");
        }

        foreach (var candidate in GetPathCandidates(tool))
        {
            if (File.Exists(candidate))
            {
                return ExternalToolResolution.Found(
                    tool,
                    displayName,
                    candidate,
                    isFromPath: true,
                    message: $"{displayName} is being used from PATH: {candidate}");
            }
        }

        return ExternalToolResolution.Missing(tool, displayName, $"{displayName} was not found.");
    }

    public string GetPreferredBundledPath(ExternalTool tool)
    {
        var firstPlatformPath = Path.Combine(
            _appBaseDirectory,
            "Resources",
            "bin",
            _platform.ResourceFolderName,
            GetFileName(tool));

        if (_platform.ResourceFolderName != "unknown")
        {
            return firstPlatformPath;
        }

        return Path.Combine(_appBaseDirectory, "Resources", "bin", GetFileName(tool));
    }

    public IReadOnlyList<string> GetBundledCandidates(ExternalTool tool)
    {
        var fileName = GetFileName(tool);
        var candidates = new List<string>();

        if (_platform.ResourceFolderName != "unknown")
        {
            candidates.Add(Path.Combine(_appBaseDirectory, "Resources", "bin", _platform.ResourceFolderName, fileName));
        }

        if (_platform.RuntimeIdentifier != "unknown" &&
            !_platform.RuntimeIdentifier.Equals(_platform.ResourceFolderName, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(_appBaseDirectory, "Resources", "bin", _platform.RuntimeIdentifier, fileName));
        }

        candidates.Add(Path.Combine(_appBaseDirectory, "Resources", "bin", fileName));
        return candidates;
    }

    private IEnumerable<string> GetPathCandidates(ExternalTool tool)
    {
        var fileName = GetFileName(tool);
        var separator = _platform.IsWindows ? ';' : ':';
        foreach (var directory in _environmentPath.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(directory, fileName);

            if (_platform.IsWindows && !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(directory, fileName + ".exe");
            }
        }
    }

    private string GetFileName(ExternalTool tool)
    {
        var baseName = tool switch
        {
            ExternalTool.YtDlp => "yt-dlp",
            ExternalTool.Ffmpeg => "ffmpeg",
            ExternalTool.Ffprobe => "ffprobe",
            ExternalTool.Aria2c => "aria2c",
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null)
        };

        return _platform.IsWindows ? baseName + ".exe" : baseName;
    }

    public static string GetDisplayName(ExternalTool tool) => tool switch
    {
        ExternalTool.YtDlp => "yt-dlp",
        ExternalTool.Ffmpeg => "ffmpeg",
        ExternalTool.Ffprobe => "ffprobe",
        ExternalTool.Aria2c => "aria2c",
        _ => tool.ToString()
    };
}
