using System.Runtime.InteropServices;

namespace Clip.Core.Tools;

public sealed record HostPlatform(HostOperatingSystem OperatingSystem, HostArchitecture Architecture)
{
    public string RuntimeIdentifier => (OperatingSystem, Architecture) switch
    {
        (HostOperatingSystem.Windows, HostArchitecture.X64) => "win-x64",
        (HostOperatingSystem.MacOS, HostArchitecture.X64) => "osx-x64",
        (HostOperatingSystem.MacOS, HostArchitecture.Arm64) => "osx-arm64",
        _ => "unknown"
    };

    public bool IsWindows => OperatingSystem == HostOperatingSystem.Windows;
    public bool IsMacOS => OperatingSystem == HostOperatingSystem.MacOS;
}

public enum HostOperatingSystem
{
    Windows,
    MacOS,
    Linux,
    Unknown
}

public enum HostArchitecture
{
    X64,
    Arm64,
    Unknown
}

public static class HostPlatformDetector
{
    public static HostPlatform Detect()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? HostOperatingSystem.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? HostOperatingSystem.MacOS
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? HostOperatingSystem.Linux
                    : HostOperatingSystem.Unknown;

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => HostArchitecture.X64,
            Architecture.Arm64 => HostArchitecture.Arm64,
            _ => HostArchitecture.Unknown
        };

        return new HostPlatform(os, architecture);
    }
}
