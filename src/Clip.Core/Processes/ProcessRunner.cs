using System.Diagnostics;
using System.Text;

namespace Clip.Core.Processes;

public sealed class ProcessRunner : IExternalProcessRunner
{
    private const int MaxBufferedLogBytes = 512 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    public async Task<ExternalProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<string>? standardOutput = null,
        Action<string>? standardError = null,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var output = new BoundedLogBuffer(MaxBufferedLogBytes);
        var error = new BoundedLogBuffer(MaxBufferedLogBytes);
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

        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            if (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                return new ExternalProcessResult(-1, output.ToString(), $"{Path.GetFileName(fileName)} timed out.");
            }

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

    private sealed class BoundedLogBuffer
    {
        private readonly int _maxBytes;
        private readonly StringBuilder _builder = new();
        private int _bytes;

        public BoundedLogBuffer(int maxBytes)
        {
            _maxBytes = maxBytes;
        }

        public void AppendLine(string line)
        {
            var text = line + Environment.NewLine;
            _builder.Append(text);
            _bytes += Encoding.UTF8.GetByteCount(text);
            while (_bytes > _maxBytes && _builder.Length > 0)
            {
                var snapshot = _builder.ToString();
                var newline = snapshot.IndexOf(Environment.NewLine, StringComparison.Ordinal);
                var removeLength = newline < 0
                    ? Math.Min(_builder.Length, _builder.Length / 2 + 1)
                    : newline + Environment.NewLine.Length;
                var removed = snapshot[..removeLength];
                _builder.Remove(0, removeLength);
                _bytes -= Encoding.UTF8.GetByteCount(removed);
            }
        }

        public override string ToString() => _builder.ToString();
    }
}
