using System.Diagnostics;
using System.Text;

namespace Clip.Services;

public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<string>? standardOutput = null,
        Action<string>? standardError = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fileName))
        {
            throw new FileNotFoundException("Executable was not found.", fileName);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(fileName) ?? ClipConstants.AppBaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PATH"] = BuildPath(startInfo.Environment.TryGetValue("PATH", out var currentPath) ? currentPath ?? "" : "");

        var output = new StringBuilder();
        var error = new StringBuilder();

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            output.AppendLine(args.Data);
            standardOutput?.Invoke(args.Data);
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            error.AppendLine(args.Data);
            standardError?.Invoke(args.Data);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {fileName}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static string BuildPath(string currentPath)
    {
        var entries = new List<string> { ClipConstants.BinDirectory };
        entries.AddRange(ClipConstants.ExtraProbePaths);
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            entries.Add(currentPath);
        }

        return string.Join(";", entries.Where(entry => !string.IsNullOrWhiteSpace(entry)));
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may already be gone; cancellation should stay quiet.
        }
    }
}
