using System.Globalization;

namespace Clip.Core.YtDlp;

public sealed class YtDlpProgressParser
{
    public bool TryParse(string? line, out YtDlpProgress progress)
    {
        progress = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        var separator = trimmed.IndexOf(':');
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            return false;
        }

        var stage = trimmed[..separator].Trim();
        var payload = trimmed[(separator + 1)..];
        var parts = payload.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 1)
        {
            return false;
        }

        var percent = ParsePercent(parts.ElementAtOrDefault(0));
        var speed = Normalize(parts.ElementAtOrDefault(1));
        var eta = Normalize(parts.ElementAtOrDefault(2));
        var status = Normalize(parts.ElementAtOrDefault(3));

        progress = new YtDlpProgress(stage, percent, speed, eta, status, trimmed);
        return true;
    }

    private static double? ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace("%", "", StringComparison.Ordinal).Trim();
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            ? Math.Clamp(percent, 0, 100)
            : null;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("N/A", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}

public readonly record struct YtDlpProgress(
    string Stage,
    double? Percent,
    string? Speed,
    string? Eta,
    string? Status,
    string RawLine);
