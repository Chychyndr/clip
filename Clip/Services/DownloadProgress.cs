namespace Clip.Services;

public sealed record DownloadProgress(
    double Percent,
    string Message,
    string? Speed = null,
    string? Eta = null,
    string? Stage = null);
