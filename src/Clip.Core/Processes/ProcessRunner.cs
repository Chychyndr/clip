using System.Diagnostics;
using System.Text;

namespace Clip.Core.Processes;

public sealed class ProcessRunner : IExternalProcessRunner
{
    public async Task<ExternalProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<string>? standardOutput = null,
        Action<string>? standardError = null,
        CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            process.StartInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

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

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ExternalProcessResult(-1, output.ToString(), ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }

        return new ExternalProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static void TryKillProcessTree(Process process)
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
            // Cancellation must not be blocked by an already exited process.
        }
    }
}
