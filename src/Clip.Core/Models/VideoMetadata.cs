using System.Text.Json.Serialization;

namespace Clip.Core.Models;

public sealed class VideoMetadata
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("webpage_url")]
    public string? WebpageUrl { get; set; }

    [JsonPropertyName("original_url")]
    public string? OriginalUrl { get; set; }

    [JsonPropertyName("extractor_key")]
    public string? ExtractorKey { get; set; }

    [JsonPropertyName("uploader")]
    public string? Uploader { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("duration")]
    public double? DurationSeconds { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("thumbnails")]
    public List<VideoThumbnail>? Thumbnails { get; set; }

    [JsonPropertyName("formats")]
    public List<FormatOption>? Formats { get; set; }

    [JsonIgnore]
    public bool IsFromCache { get; set; }

    public string Author => FirstNonEmpty(Channel, Uploader, "Unknown creator");
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled video" : Title;
    public string DurationLabel => DurationSeconds is > 0 ? ClipRange.FormatTime(DurationSeconds.Value) : "Unknown";
    public string BestThumbnail => FirstNonEmpty(Thumbnail, Thumbnails?.LastOrDefault(t => !string.IsNullOrWhiteSpace(t.Url))?.Url, "");

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}

public sealed class VideoThumbnail
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }
}
