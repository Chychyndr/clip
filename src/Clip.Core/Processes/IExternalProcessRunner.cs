namespace Clip.Core.Processes;

public interface IExternalProcessRunner
{
    Task<ExternalProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        Action<string>? standardOutput = null,
        Action<string>? standardError = null,
        CancellationToken cancellationToken = default);
}

public sealed record ExternalProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}
