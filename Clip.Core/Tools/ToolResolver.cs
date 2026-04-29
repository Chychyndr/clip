using System.Diagnostics;

namespace Clip.Core.Tools;

public sealed class ToolResolver
{
    private readonly string _appBaseDirectory;
    private readonly HostPlatform _platform;
    private readonly string _environmentPath;

    public ToolResolver(
        string? appBaseDirectory = null,
        HostPlatform? platform = null,
        string? environmentPath = null)
    {
        _appBaseDirectory = appBaseDirectory ?? AppContext.BaseDirectory;
        _platform = platform ?? HostPlatformDetector.Detect();
        _environmentPath = environmentPath ?? Environment.GetEnvironmentVariable("PATH") ?? "";
    }

    public HostPlatform Platform => _platform;

    public ExternalToolResolution Resolve(ExternalTool tool, bool ensureExecutable = true)
    {
        var displayName = GetDisplayName(tool);
        foreach (var candidate in GetBundledCandidates(tool))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var permissionMessage = ensureExecutable
                ? TryEnsureExecutable(candidate)
                : null;

            return ExternalToolResolution.Found(
                tool,
                displayName,
                candidate,
                isFromPath: false,
                permissionMessage);
        }

        foreach (var candidate in GetPathCandidates(tool))
        {
            if (File.Exists(candidate))
            {
                return ExternalToolResolution.Found(tool, displayName, candidate, isFromPath: true);
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
            _platform.RuntimeIdentifier,
            GetFileName(tool));

        if (_platform.RuntimeIdentifier != "unknown")
        {
            return firstPlatformPath;
        }

        return Path.Combine(_appBaseDirectory, "Resources", "bin", GetFileName(tool));
    }

    public IReadOnlyList<string> GetBundledCandidates(ExternalTool tool)
    {
        var fileName = GetFileName(tool);
        var candidates = new List<string>();

        if (_platform.RuntimeIdentifier != "unknown")
        {
            candidates.Add(Path.Combine(_appBaseDirectory, "Resources", "bin", _platform.RuntimeIdentifier, fileName));
        }

        candidates.Add(Path.Combine(_appBaseDirectory, "Resources", "bin", fileName));
        candidates.Add(Path.Combine(_appBaseDirectory, fileName));
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

    private string? TryEnsureExecutable(string path)
    {
        if (!_platform.IsMacOS)
        {
            return null;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return null;
            }

            var mode = File.GetUnixFileMode(path);
            const UnixFileMode executableBits =
                UnixFileMode.UserExecute |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherExecute;

            if ((mode & executableBits) != 0)
            {
                return null;
            }

            File.SetUnixFileMode(path, mode | executableBits);
            return null;
        }
        catch
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("+x");
                startInfo.ArgumentList.Add(path);

                using var process = Process.Start(startInfo);
                process?.WaitForExit(3000);
            }
            catch
            {
                return $"{System.IO.Path.GetFileName(path)} exists but Clip could not grant execute permission.";
            }
        }

        return null;
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
