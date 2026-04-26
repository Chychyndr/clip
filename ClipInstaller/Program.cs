using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClipInstaller;

internal static class Program
{
    private const string AppName = "Clip";

    [STAThread]
    private static int Main(string[] args)
    {
        var silent = args.Any(argument =>
            string.Equals(argument, "/silent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "--silent", StringComparison.OrdinalIgnoreCase));

        try
        {
            var installDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                AppName);

            KillRunningClip();
            InstallPayload(installDirectory);
            CreateStartMenuShortcut(installDirectory);
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

    private static void InstallPayload(string installDirectory)
    {
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

    private static void KillRunningClip()
    {
        foreach (var process in Process.GetProcessesByName("Clip"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
                // A locked previous instance will be handled by the install step.
            }
        }
    }

    private static void CreateStartMenuShortcut(string installDirectory)
    {
        var shortcutDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            AppName);
        Directory.CreateDirectory(shortcutDirectory);

        var shortcutPath = Path.Combine(shortcutDirectory, "Clip.lnk");
        var exePath = Path.Combine(installDirectory, "Clip.exe");

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = exePath;
        shortcut.WorkingDirectory = installDirectory;
        shortcut.Description = "Clip";
        shortcut.IconLocation = exePath;
        shortcut.Save();
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
