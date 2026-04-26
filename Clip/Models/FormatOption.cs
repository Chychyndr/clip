using System.Text.Json.Serialization;

namespace Clip.Models;

public sealed class FormatOption
{
    [JsonPropertyName("format_id")]
    public string? FormatId { get; set; }

    [JsonPropertyName("format_note")]
    public string? FormatNote { get; set; }

    [JsonPropertyName("ext")]
    public string? Extension { get; set; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("filesize")]
    public long? FileSize { get; set; }

    [JsonPropertyName("filesize_approx")]
    public long? ApproximateFileSize { get; set; }

    [JsonPropertyName("vcodec")]
    public string? VideoCodec { get; set; }

    [JsonPropertyName("acodec")]
    public string? AudioCodec { get; set; }

    [JsonPropertyName("fps")]
    public double? FramesPerSecond { get; set; }

    public string DisplayName
    {
        get
        {
            var resolution = Height is > 0 ? $"{Height}p" : Resolution;
            var ext = string.IsNullOrWhiteSpace(Extension) ? "media" : Extension.ToUpperInvariant();
            return string.IsNullOrWhiteSpace(resolution) ? ext : $"{resolution} {ext}";
        }
    }
}
