using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClipInstaller;

internal static class Program
{
    private const string AppName = "Clip";
    private const string UninstallerFileName = "ClipUninstall.exe";
    private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Clip";

    [STAThread]
    private static int Main(string[] args)
    {
        var silent = HasArgument(args, "/silent", "--silent");
        var uninstall = HasArgument(args, "/uninstall", "--uninstall");

        try
        {
            var installDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                AppName);

            if (uninstall)
            {
                Uninstall(installDirectory, silent);
                return 0;
            }

            KillRunningClip(installDirectory);
            InstallPayload(installDirectory);
            InstallUninstaller(installDirectory);
            CreateStartMenuShortcuts(installDirectory);
            RegisterUninstallEntry(installDirectory);
            LaunchClip(installDirectory);
            if (!silent)
            {
                MessageBox(IntPtr.Zero, "Clip установлен и запущен.", "Clip Setup", 0x00000040);
            }

            return 0;
        }
        catch (Exception exception)
        {
            if (!silent)
            {
                MessageBox(IntPtr.Zero, exception.Message, "Clip Setup", 0x00000010);
            }

            return 1;
        }
    }

    private static bool HasArgument(string[] args, params string[] names)
    {
        return args.Any(argument => names.Any(name => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static void Uninstall(string installDirectory, bool silent)
    {
        RemoveStartMenuShortcuts();
        RemoveUninstallEntry();
        KillRunningClip(installDirectory);
        var deletionWasScheduled = DeleteInstallDirectory(installDirectory);

        if (!silent)
        {
            var message = deletionWasScheduled
                ? "Clip удален. Папка приложения будет очищена после закрытия этого окна."
                : "Clip удален.";
            MessageBox(IntPtr.Zero, message, "Clip Setup", 0x00000040);
        }
    }

    private static void InstallPayload(string installDirectory)
    {
        EnsureSafeInstallDirectory(installDirectory);

        var tempDirectory = Path.Combine(Path.GetTempPath(), "ClipSetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var zipPath = Path.Combine(tempDirectory, "clip-payload.zip");
            using (var payload = OpenPayload())
            using (var file = File.Create(zipPath))
            {
                payload.CopyTo(file);
            }

            var extractDirectory = Path.Combine(tempDirectory, "app");
            ZipFile.ExtractToDirectory(zipPath, extractDirectory, overwriteFiles: true);

            if (Directory.Exists(installDirectory))
            {
                Directory.Delete(installDirectory, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(installDirectory)!);
            Directory.Move(extractDirectory, installDirectory);
        }
        finally
        {
            TryDelete(tempDirectory);
        }
    }

    private static void InstallUninstaller(string installDirectory)
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
        {
            throw new FileNotFoundException("Не удалось подготовить деинсталлятор Clip.");
        }

        var uninstallerPath = Path.Combine(installDirectory, UninstallerFileName);
        if (string.Equals(Path.GetFullPath(currentPath), Path.GetFullPath(uninstallerPath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        File.Copy(currentPath, uninstallerPath, overwrite: true);
    }

    private static Stream OpenPayload()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream("clip-payload.zip");
        if (stream is null)
        {
            throw new FileNotFoundException("В установщик не встроен payload приложения.");
        }

        return stream;
    }

    private static void EnsureSafeInstallDirectory(string installDirectory)
    {
        var programsDirectory = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs"));
        var targetDirectory = Path.GetFullPath(installDirectory);
        var relativeTarget = Path.GetRelativePath(programsDirectory, targetDirectory);

        if (string.IsNullOrWhiteSpace(relativeTarget) ||
            relativeTarget.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeTarget))
        {
            throw new InvalidOperationException("Небезопасный путь установки.");
        }
    }

    private static void KillRunningClip(string installDirectory)
    {
        foreach (var process in Process.GetProcessesByName("Clip"))
        {
            try
            {
                if (!IsInstalledClipProcess(process, installDirectory))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
                // A locked previous instance will be handled by the install step.
            }
        }
    }

    private static bool IsInstalledClipProcess(Process process, string installDirectory)
    {
        try
        {
            var expectedPath = Path.GetFullPath(Path.Combine(installDirectory, "Clip.exe"));
            var actualPath = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(actualPath) &&
                   string.Equals(Path.GetFullPath(actualPath), expectedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void CreateStartMenuShortcuts(string installDirectory)
    {
        var shortcutDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            AppName);
        Directory.CreateDirectory(shortcutDirectory);

        var exePath = Path.Combine(installDirectory, "Clip.exe");
        var uninstallerPath = Path.Combine(installDirectory, UninstallerFileName);

        CreateShortcut(
            Path.Combine(shortcutDirectory, "Clip.lnk"),
            exePath,
            installDirectory,
            "Clip",
            exePath);

        CreateShortcut(
            Path.Combine(shortcutDirectory, "Uninstall Clip.lnk"),
            uninstallerPath,
            installDirectory,
            "Uninstall Clip",
            exePath,
            "/uninstall");
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string description,
        string iconLocation,
        string arguments = "")
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = description;
        shortcut.IconLocation = iconLocation;
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            shortcut.Arguments = arguments;
        }

        shortcut.Save();
    }

    private static void RemoveStartMenuShortcuts()
    {
        var shortcutDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            AppName);
        TryDelete(shortcutDirectory);
    }

    private static void RegisterUninstallEntry(string installDirectory)
    {
        var exePath = Path.Combine(installDirectory, "Clip.exe");
        var uninstallerPath = Path.Combine(installDirectory, UninstallerFileName);

        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryPath);
        if (key is null)
        {
            return;
        }

        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayIcon", $"{exePath},0");
        key.SetValue("DisplayVersion", "1.0.0");
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("Publisher", AppName);
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" /uninstall /silent");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", GetEstimatedSizeInKilobytes(installDirectory), RegistryValueKind.DWord);
    }

    private static void RemoveUninstallEntry()
    {
        Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);
    }

    private static void LaunchClip(string installDirectory)
    {
        var exePath = Path.Combine(installDirectory, "Clip.exe");
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("Clip.exe не найден после установки.", exePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = installDirectory,
            UseShellExecute = true
        });
    }

    private static bool DeleteInstallDirectory(string installDirectory)
    {
        if (!Directory.Exists(installDirectory))
        {
            return false;
        }

        EnsureSafeInstallDirectory(installDirectory);

        if (IsCurrentProcessInDirectory(installDirectory))
        {
            ScheduleInstallDirectoryDeletion(installDirectory);
            return true;
        }

        Directory.Delete(installDirectory, recursive: true);
        return false;
    }

    private static bool IsCurrentProcessInDirectory(string directory)
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        var directoryPath = EnsureTrailingDirectorySeparator(Path.GetFullPath(directory));
        var processPath = Path.GetFullPath(currentPath);
        return processPath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void ScheduleInstallDirectoryDeletion(string installDirectory)
    {
        var script =
            $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
            "Start-Sleep -Milliseconds 300; " +
            $"Remove-Item -LiteralPath {QuotePowerShellLiteral(installDirectory)} -Recurse -Force -ErrorAction SilentlyContinue";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);
        Process.Start(startInfo);
    }

    private static int GetEstimatedSizeInKilobytes(string directory)
    {
        long bytes = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                bytes += new FileInfo(file).Length;
            }
            catch
            {
                // Size is only metadata for Windows Settings.
            }
        }

        return (int)Math.Min(int.MaxValue, bytes / 1024);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static string QuotePowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary files can be cleaned by Windows later.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
