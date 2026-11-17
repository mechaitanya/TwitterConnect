using System.Text.Json.Serialization;

namespace XConnect.Models;

/// <summary>A media object expanded through <c>attachments.media_keys</c>.</summary>
public sealed record XMedia
{
    /// <summary>The media key.</summary>
    public string? MediaKey { get; init; }

    /// <summary>One of <c>photo</c>, <c>video</c> or <c>animated_gif</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Direct URL (photos).</summary>
    public string? Url { get; init; }

    /// <summary>Preview image URL.</summary>
    public string? PreviewImageUrl { get; init; }

    /// <summary>Alt text.</summary>
    public string? AltText { get; init; }

    /// <summary>Width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Height in pixels.</summary>
    public int? Height { get; init; }

    /// <summary>Duration in milliseconds.</summary>
    public int? DurationMs { get; init; }

    /// <summary>Video variants.</summary>
    public IReadOnlyList<XMediaVariant>? Variants { get; init; }

    /// <summary>Public view counts.</summary>
    public XMediaPublicMetrics? PublicMetrics { get; init; }

    /// <summary>Playback figures visible only to the owner (user context).</summary>
    public XMediaPlaybackMetrics? NonPublicMetrics { get; init; }

    /// <summary>Organic metrics (user context).</summary>
    public XMediaOrganicMetrics? OrganicMetrics { get; init; }

    /// <summary>Promoted metrics (user context).</summary>
    public XMediaPromotedMetrics? PromotedMetrics { get; init; }

    /// <summary>Revenue earned by the media.</summary>
    public XMediaRevenue? Revenue { get; init; }
}

/// <summary>A video variant.</summary>
public sealed record XMediaVariant
{
    /// <summary>Bit rate.</summary>
    public int? BitRate { get; init; }

    /// <summary>MIME type.</summary>
    public string? ContentType { get; init; }

    /// <summary>URL.</summary>
    public string? Url { get; init; }
}

/// <summary>Media category: decides size limits and where the media can be used.</summary>
public enum XMediaCategory
{
    /// <summary>Image for a Post (<c>tweet_image</c>).</summary>
    TweetImage,

    /// <summary>Animated GIF for a Post (<c>tweet_gif</c>).</summary>
    TweetGif,

    /// <summary>Video for a Post (<c>tweet_video</c>).</summary>
    TweetVideo,

    /// <summary>Image for a Direct Message (<c>dm_image</c>).</summary>
    DmImage,

    /// <summary>Animated GIF for a Direct Message (<c>dm_gif</c>).</summary>
    DmGif,

    /// <summary>Video for a Direct Message (<c>dm_video</c>).</summary>
    DmVideo,

    /// <summary>A subtitle file (<c>subtitles</c>): <c>text/srt</c> or <c>text/vtt</c>, up to 1 MB. Attach it to a video with <c>CreateSubtitlesAsync</c>.</summary>
    Subtitles,
}

/// <summary>The video category a subtitle track is attached to.</summary>
public enum XSubtitleCategory
{
    /// <summary>A video uploaded for a Post.</summary>
    TweetVideo,

    /// <summary>An Amplify video.</summary>
    AmplifyVideo,
}

/// <summary>Metadata to attach to uploaded media. Set only what you need; unset properties are not sent.</summary>
public sealed record XMediaMetadata
{
    /// <summary>Alt text, up to 1000 characters.</summary>
    public string? AltText { get; init; }

    /// <summary>Whether viewers may download the media.</summary>
    public bool? AllowDownload { get; init; }

    /// <summary>When the media should expire, in Unix seconds.</summary>
    public double? ContentExpirationTimestampSec { get; init; }

    /// <summary>Domains that may embed the media.</summary>
    public IReadOnlyList<string>? DomainWhitelist { get; init; }

    /// <summary>The provider and ID of media found through a third-party provider.</summary>
    public XFoundMediaOrigin? FoundMediaOrigin { get; init; }

    /// <summary>Marks the media as managed.</summary>
    public bool? Managed { get; init; }

    /// <summary>Marks the media as shared.</summary>
    public bool? Shared { get; init; }

    /// <summary>The upload source label.</summary>
    public string? UploadSource { get; init; }
}

/// <summary>Origin of media found through a provider.</summary>
public sealed record XFoundMediaOrigin
{
    /// <summary>The provider name.</summary>
    public required string Provider { get; init; }

    /// <summary>The ID at the provider.</summary>
    public required string Id { get; init; }
}

/// <summary>Result of attaching subtitles.</summary>
public sealed record XMediaSubtitlesResult
{
    /// <summary>The video's media ID.</summary>
    public string? Id { get; init; }

    /// <summary>The category.</summary>
    public string? MediaCategory { get; init; }

    /// <summary>The associated subtitle tracks, as returned by X.</summary>
    public System.Text.Json.JsonElement? AssociatedSubtitles { get; init; }
}

/// <summary>Result of deleting subtitles.</summary>
public sealed record XMediaSubtitlesDeleteResult
{
    /// <summary>True when deleted.</summary>
    public bool Deleted { get; init; }
}

/// <summary>Processing state reported after finalizing an upload.</summary>
public sealed record XMediaProcessingInfo
{
    /// <summary>One of <c>pending</c>, <c>in_progress</c>, <c>succeeded</c> or <c>failed</c>.</summary>
    public string? State { get; init; }

    /// <summary>Seconds to wait before polling again.</summary>
    public int? CheckAfterSecs { get; init; }

    /// <summary>Progress percentage.</summary>
    public int? ProgressPercent { get; init; }

    /// <summary>True when processing finished successfully.</summary>
    [JsonIgnore]
    public bool IsSucceeded => string.Equals(State, "succeeded", StringComparison.Ordinal);

    /// <summary>True when processing failed.</summary>
    [JsonIgnore]
    public bool IsFailed => string.Equals(State, "failed", StringComparison.Ordinal);
}

/// <summary>The wire shape of the append response.</summary>
internal sealed record XMediaAppendData
{
    public long? ExpiresAt { get; init; }
}

/// <summary>Result of attaching metadata such as alt text.</summary>
public sealed record XMediaMetadataResult
{
    /// <summary>The media ID.</summary>
    public string? Id { get; init; }
}

/// <summary>Thrown when X reports that asynchronous media processing failed or timed out.</summary>
public sealed class XMediaProcessingException : Exception
{
    /// <summary>Creates the exception.</summary>
    public XMediaProcessingException(string message, string? mediaId, XMediaProcessingInfo? info)
        : base(message)
    {
        MediaId = mediaId;
        ProcessingInfo = info;
    }

    /// <summary>The media ID.</summary>
    public string? MediaId { get; }

    /// <summary>The last processing information X reported.</summary>
    public XMediaProcessingInfo? ProcessingInfo { get; }
}

/// <summary>The outcome of an upload, ready to attach to a Post or Direct Message.</summary>
public sealed record XMediaUploadResult
{
    /// <summary>The media ID to pass in <c>media.media_ids</c>.</summary>
    [JsonPropertyName("id")]
    public required string MediaId { get; init; }

    /// <summary>The media key.</summary>
    public string? MediaKey { get; init; }

    /// <summary>Size in bytes, if X reported it.</summary>
    public long? Size { get; init; }

    /// <summary>How long the upload stays usable, in seconds.</summary>
    public int? ExpiresAfterSecs { get; init; }

    /// <summary>Final processing info, when the upload needed asynchronous processing.</summary>
    public XMediaProcessingInfo? ProcessingInfo { get; init; }

    /// <summary>Image details, when the upload is an image.</summary>
    public XMediaImageInfo? Image { get; init; }

    /// <summary>Video details, when the upload is a video.</summary>
    public XMediaVideoInfo? Video { get; init; }
}

/// <summary>Progress of an in-flight upload.</summary>
/// <param name="BytesSent">Bytes handed to X so far.</param>
/// <param name="TotalBytes">Total bytes to send.</param>
public readonly record struct XUploadProgress(long BytesSent, long TotalBytes);

/// <summary>Options for a media upload.</summary>
public sealed record XMediaUploadOptions
{
    /// <summary>Media category. Required.</summary>
    public XMediaCategory Category { get; init; } = XMediaCategory.TweetImage;

    /// <summary>MIME type. When <see langword="null"/> it is inferred from <see cref="FileName"/>.</summary>
    public string? ContentType { get; init; }

    /// <summary>File name used to infer the content type and for the multipart part.</summary>
    public string? FileName { get; init; }

    /// <summary>User IDs that may also use the media.</summary>
    public IReadOnlyList<string>? AdditionalOwners { get; init; }

    /// <summary>Segment size for chunked uploads. Defaults to 4 MB; X accepts up to 5 MB per the documentation.</summary>
    public int SegmentSize { get; init; } = 4 * 1024 * 1024;

    /// <summary>How long to wait for asynchronous processing. Defaults to 10 minutes.</summary>
    public TimeSpan ProcessingTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Receives progress callbacks.</summary>
    public IProgress<XUploadProgress>? Progress { get; init; }
}

/// <summary>Public media metrics.</summary>
public sealed record XMediaPublicMetrics
{
    /// <summary>Views.</summary>
    public long? ViewCount { get; init; }
}

/// <summary>Playback quartile counts.</summary>
public record XMediaPlaybackMetrics
{
    /// <summary>Playbacks that started.</summary>
    [JsonPropertyName("playback_0_count")]
    public long? Playback0Count { get; init; }

    /// <summary>Playbacks that reached 25 percent.</summary>
    [JsonPropertyName("playback_25_count")]
    public long? Playback25Count { get; init; }

    /// <summary>Playbacks that reached 50 percent.</summary>
    [JsonPropertyName("playback_50_count")]
    public long? Playback50Count { get; init; }

    /// <summary>Playbacks that reached 75 percent.</summary>
    [JsonPropertyName("playback_75_count")]
    public long? Playback75Count { get; init; }

    /// <summary>Playbacks that reached 100 percent.</summary>
    [JsonPropertyName("playback_100_count")]
    public long? Playback100Count { get; init; }
}

/// <summary>Organic media metrics.</summary>
public sealed record XMediaOrganicMetrics : XMediaPlaybackMetrics
{
    /// <summary>Views.</summary>
    public long? ViewCount { get; init; }

    /// <summary>Total watch time in milliseconds.</summary>
    public long? WatchTimeMs { get; init; }

    /// <summary>Call-to-action URL clicks.</summary>
    public long? CtaUrlClickCount { get; init; }

    /// <summary>Call-to-action watch clicks.</summary>
    public long? CtaWatchClickCount { get; init; }
}

/// <summary>Promoted media metrics.</summary>
public sealed record XMediaPromotedMetrics : XMediaPlaybackMetrics
{
    /// <summary>Views.</summary>
    public long? ViewCount { get; init; }
}

/// <summary>Revenue earned by media.</summary>
public sealed record XMediaRevenue
{
    /// <summary>The amount in millionths of the currency unit.</summary>
    public long? AmountMicros { get; init; }

    /// <summary>The ISO 4217 currency code.</summary>
    public string? CurrencyCode { get; init; }
}

/// <summary>Image details of an upload.</summary>
public sealed record XMediaImageInfo
{
    /// <summary>The detected image type.</summary>
    public string? ImageType { get; init; }

    /// <summary>Width in pixels.</summary>
    [JsonPropertyName("w")]
    public int? Width { get; init; }

    /// <summary>Height in pixels.</summary>
    [JsonPropertyName("h")]
    public int? Height { get; init; }
}

/// <summary>Video details of an upload.</summary>
public sealed record XMediaVideoInfo
{
    /// <summary>The detected video type.</summary>
    public string? VideoType { get; init; }
}
