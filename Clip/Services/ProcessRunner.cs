using System.Diagnostics;
using System.Text;
using Clip.Core.Processes;

namespace Clip.Services;

public sealed class ProcessRunner : IExternalProcessRunner
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

        CrashLog.Info($"Starting process: {Path.GetFileName(fileName)} {SanitizeArguments(arguments)}");

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

        var processResult = new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
        if (!processResult.IsSuccess)
        {
            CrashLog.Info($"{Path.GetFileName(fileName)} exited with code {process.ExitCode}.");
        }

        return processResult;
    }

    async Task<ExternalProcessResult> IExternalProcessRunner.RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory,
        Action<string>? standardOutput,
        Action<string>? standardError,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(fileName, arguments, workingDirectory, standardOutput, standardError, cancellationToken);
        return new ExternalProcessResult(result.ExitCode, result.StandardOutput, result.StandardError);
    }

    private static string BuildPath(string currentPath)
    {
        var entries = new List<string> { ClipConstants.BinDirectory, ClipConstants.LegacyBinDirectory };
        entries.AddRange(ClipConstants.ExtraProbePaths);
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            entries.Add(currentPath);
        }

        return string.Join(Path.PathSeparator, entries.Where(entry => !string.IsNullOrWhiteSpace(entry)));
    }

    private static string SanitizeArguments(IEnumerable<string> arguments)
    {
        var sanitized = new List<string>();
        var redactNext = false;
        foreach (var argument in arguments)
        {
            if (redactNext)
            {
                sanitized.Add("<redacted>");
                redactNext = false;
                continue;
            }

            sanitized.Add(ShouldRedact(argument) ? "<redacted>" : SanitizeUrl(argument));
            redactNext = argument.Contains("cookies", StringComparison.OrdinalIgnoreCase) ||
                         argument.Contains("header", StringComparison.OrdinalIgnoreCase) ||
                         argument.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                         argument.Contains("token", StringComparison.OrdinalIgnoreCase);
        }

        return string.Join(" ", sanitized);
    }

    private static bool ShouldRedact(string argument) =>
        argument.Contains("cookie:", StringComparison.OrdinalIgnoreCase) ||
        argument.Contains("authorization:", StringComparison.OrdinalIgnoreCase) ||
        argument.Contains("token=", StringComparison.OrdinalIgnoreCase) ||
        argument.Contains("password=", StringComparison.OrdinalIgnoreCase);

    private static string SanitizeUrl(string argument)
    {
        if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Query))
        {
            return argument;
        }

        return new UriBuilder(uri) { Query = "query=redacted" }.Uri.AbsoluteUri;
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
