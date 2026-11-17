using System.Globalization;
using System.Net.Http.Json;
using XConnect.Authentication;
using XConnect.Infrastructure;
using XConnect.Models;
using XConnect.Serialization;

namespace XConnect.Clients;

/// <summary>
/// Uploads media with the X API v2 media endpoints and chooses the workflow for you: images use a single
/// multipart request; GIFs and videos use the chunked INIT, APPEND and FINALIZE flow followed by status polling.
/// Streams are read in segments, so large videos are never loaded into memory.
/// </summary>
/// <remarks>
/// Upload requests are not retried automatically (other than after HTTP 429). Failed API calls throw
/// <see cref="XApiException"/>, whose <see cref="XApiException.Error"/> is inspectable. Uploading requires user-context
/// authentication (OAuth 1.0a, or OAuth 2.0 with the <c>media.write</c> scope).
/// </remarks>
public interface IXMediaClient
{
    /// <summary>Uploads a stream. The workflow is chosen from <see cref="XMediaUploadOptions.Category"/> and the stream length.</summary>
    /// <param name="content">The media. Chunked uploads (GIF and video) require a seekable stream so the size is known up front.</param>
    /// <param name="options">The category, content type and progress callback.</param>
    /// <param name="cancellationToken">A token to cancel the upload.</param>
    /// <exception cref="ArgumentException">The content type, size or stream is not valid for the category.</exception>
    /// <exception cref="XApiException">X rejected a step of the upload.</exception>
    /// <exception cref="XMediaProcessingException">X failed to process the media, or processing timed out.</exception>
    Task<XMediaUploadResult> UploadAsync(Stream content, XMediaUploadOptions options, CancellationToken cancellationToken = default);

    /// <summary>Uploads an image for a Post. The content type is inferred from <paramref name="fileName"/>.</summary>
    Task<XMediaUploadResult> UploadImageAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Uploads an animated GIF for a Post.</summary>
    Task<XMediaUploadResult> UploadGifAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Uploads a video for a Post and waits until it is processed.</summary>
    Task<XMediaUploadResult> UploadVideoAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Gets the processing status of an upload (<c>GET /2/media/upload?command=STATUS</c>).</summary>
    Task<XResponse<XMediaUploadResult>> GetUploadStatusAsync(string mediaId, CancellationToken cancellationToken = default);

    /// <summary>Polls until processing succeeds. Create the Post only after this returns.</summary>
    /// <exception cref="XMediaProcessingException">Processing failed or <paramref name="timeout"/> elapsed.</exception>
    Task<XMediaUploadResult> WaitForProcessingAsync(string mediaId, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>Sets alt text for uploaded media (<c>POST /2/media/metadata</c>). The API limits it to 1000 characters.</summary>
    Task<XResponse<XMediaMetadataResult>> SetAltTextAsync(string mediaId, string altText, CancellationToken cancellationToken = default);

    /// <summary>Attaches metadata to uploaded media (<c>POST /2/media/metadata</c>). Sticker, audience, geo-restriction, sensitive-media and preview-image metadata are untyped in the specification and are not supported.</summary>
    Task<XResponse<XMediaMetadataResult>> SetMetadataAsync(string mediaId, XMediaMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>Attaches an uploaded subtitle file (category <see cref="XMediaCategory.Subtitles"/>) to a video (<c>POST /2/media/subtitles</c>).</summary>
    /// <param name="videoMediaId">The video's media ID.</param>
    /// <param name="category">The video's category.</param>
    /// <param name="subtitleMediaId">The media ID of the uploaded subtitle file.</param>
    /// <param name="languageCode">For example <c>en</c>.</param>
    /// <param name="displayName">The name shown to viewers.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<XResponse<XMediaSubtitlesResult>> CreateSubtitlesAsync(string videoMediaId, XSubtitleCategory category, string subtitleMediaId, string languageCode, string displayName, CancellationToken cancellationToken = default);

    /// <summary>Removes a subtitle track from a video (<c>DELETE /2/media/subtitles</c>).</summary>
    Task<XResponse<XMediaSubtitlesDeleteResult>> DeleteSubtitlesAsync(string videoMediaId, XSubtitleCategory category, string languageCode, CancellationToken cancellationToken = default);

    /// <summary>Looks up media by key (<c>GET /2/media/{media_key}</c>). Use <see cref="XFieldSelection.MediaFields"/> to choose fields.</summary>
    Task<XResponse<XMedia>> GetAsync(string mediaKey, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Looks up several media items by key (<c>GET /2/media</c>).</summary>
    Task<XResponse<IReadOnlyList<XMedia>>> GetManyAsync(IReadOnlyList<string> mediaKeys, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets video analytics (<c>GET /2/media/analytics</c>). Requires user-context authentication.</summary>
    /// <param name="mediaKeys">The media keys.</param>
    /// <param name="startTime">Start of the window.</param>
    /// <param name="endTime">End of the window.</param>
    /// <param name="granularity">One of <c>hourly</c>, <c>daily</c> or <c>total</c>.</param>
    /// <param name="metrics">The <c>media_analytics.fields</c> to return, for example <c>video_views</c>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<XResponse<IReadOnlyList<XMediaAnalytics>>> GetAnalyticsAsync(IReadOnlyList<string> mediaKeys, DateTimeOffset startTime, DateTimeOffset endTime, string? granularity = null, IReadOnlyList<string>? metrics = null, CancellationToken cancellationToken = default);
}

internal sealed class XMediaClient(XApiTransport transport) : IXMediaClient
{
    private const long MiB = 1024 * 1024;
    private const int MaxSegmentSize = 5 * (int)MiB;
    private const int MaxSegments = 10_000;

    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XMediaUploadResult> UploadImageAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
        UploadAsync(content, new XMediaUploadOptions { Category = XMediaCategory.TweetImage, FileName = fileName }, cancellationToken);

    public Task<XMediaUploadResult> UploadGifAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
        UploadAsync(content, new XMediaUploadOptions { Category = XMediaCategory.TweetGif, FileName = fileName }, cancellationToken);

    public Task<XMediaUploadResult> UploadVideoAsync(Stream content, string fileName, CancellationToken cancellationToken = default) =>
        UploadAsync(content, new XMediaUploadOptions { Category = XMediaCategory.TweetVideo, FileName = fileName }, cancellationToken);

    public async Task<XMediaUploadResult> UploadAsync(Stream content, XMediaUploadOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        if (!content.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(content));
        }

        if (options.SegmentSize is < 1 or > MaxSegmentSize)
        {
            throw new ArgumentException($"SegmentSize must be between 1 and {MaxSegmentSize} bytes (X documents 5 MB per segment).", nameof(options));
        }

        var contentType = options.ContentType ?? InferContentType(options.FileName)
            ?? throw new ArgumentException("Provide ContentType, or a FileName with a known extension.", nameof(options));
        ValidateContentType(options.Category, contentType);

        var limit = MaxBytes(options.Category);
        long? length = content.CanSeek ? content.Length - content.Position : null;
        if (length > limit)
        {
            throw new ArgumentException($"{length} bytes exceeds the {limit}-byte limit for {CategoryName(options.Category)}.", nameof(content));
        }

        if (length == 0)
        {
            throw new ArgumentException("The stream is empty.", nameof(content));
        }

        var result = options.Category is XMediaCategory.TweetImage or XMediaCategory.DmImage or XMediaCategory.Subtitles
            ? await UploadSimpleAsync(content, options, contentType, limit, cancellationToken).ConfigureAwait(false)
            : await UploadChunkedAsync(content, options, contentType, length, cancellationToken).ConfigureAwait(false);

        return result.ProcessingInfo is { IsSucceeded: false }
            ? await PollAsync(result.MediaId, options.ProcessingTimeout, result.ProcessingInfo, cancellationToken).ConfigureAwait(false)
            : result;
    }

    public async Task<XResponse<XMediaUploadResult>> GetUploadStatusAsync(string mediaId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        var request = XApiRequest.Create(HttpMethod.Get, "/2/media/upload", XAuthMethods.UserContext)
            .Add("command", "STATUS")
            .Add("media_id", mediaId);
        return await transport.SendAsync(request, Json.EnvelopeXMediaUploadResult, cancellationToken).ConfigureAwait(false);
    }

    public Task<XMediaUploadResult> WaitForProcessingAsync(string mediaId, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        PollAsync(mediaId, timeout ?? TimeSpan.FromMinutes(10), null, cancellationToken);

    public Task<XResponse<XMediaMetadataResult>> SetAltTextAsync(string mediaId, string altText, CancellationToken cancellationToken = default) =>
        SetMetadataAsync(mediaId, new XMediaMetadata { AltText = altText }, cancellationToken);

    public Task<XResponse<XMediaMetadataResult>> SetMetadataAsync(string mediaId, XMediaMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.AltText is { } alt && (string.IsNullOrWhiteSpace(alt) || alt.Length > 1000))
        {
            throw new ArgumentException("Alt text must be 1 to 1000 characters.", nameof(metadata));
        }

        var payload = new MetadataPayload(
            metadata.AltText is null ? null : new AltTextPayload(metadata.AltText),
            metadata.AllowDownload is null ? null : new AllowDownloadPayload(metadata.AllowDownload.Value),
            metadata.ContentExpirationTimestampSec is null ? null : new ExpirationPayload(metadata.ContentExpirationTimestampSec.Value),
            metadata.DomainWhitelist is null ? null : new DomainPayload(metadata.DomainWhitelist),
            metadata.FoundMediaOrigin is null ? null : new OriginPayload(metadata.FoundMediaOrigin.Provider, metadata.FoundMediaOrigin.Id),
            metadata.Managed is null ? null : new ManagedPayload(metadata.Managed.Value),
            metadata.Shared is null ? null : new SharedPayload(metadata.Shared.Value),
            metadata.UploadSource is null ? null : new UploadSourcePayload(metadata.UploadSource));
        if (payload == new MetadataPayload(null, null, null, null, null, null, null, null))
        {
            throw new ArgumentException("Set at least one metadata property.", nameof(metadata));
        }

        var request = XApiRequest.Create(HttpMethod.Post, "/2/media/metadata", XAuthMethods.UserContext)
            .WithContent(() => JsonContent.Create(new MetadataBody(mediaId, payload), Json.MetadataBody));
        return transport.SendAsync(request, Json.EnvelopeXMediaMetadataResult, cancellationToken);
    }

    public Task<XResponse<XMediaSubtitlesResult>> CreateSubtitlesAsync(string videoMediaId, XSubtitleCategory category, string subtitleMediaId, string languageCode, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoMediaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subtitleMediaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var request = XApiRequest.Create(HttpMethod.Post, "/2/media/subtitles", XAuthMethods.UserContext)
            .WithContent(() => JsonContent.Create(
                new SubtitlesBody(videoMediaId, SubtitleCategoryName(category), new SubtitlePayload(displayName, subtitleMediaId, languageCode)),
                Json.SubtitlesBody));
        return transport.SendAsync(request, Json.EnvelopeXMediaSubtitlesResult, cancellationToken);
    }

    public Task<XResponse<XMediaSubtitlesDeleteResult>> DeleteSubtitlesAsync(string videoMediaId, XSubtitleCategory category, string languageCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoMediaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        var request = XApiRequest.Create(HttpMethod.Delete, "/2/media/subtitles", XAuthMethods.UserContext)
            .WithContent(() => JsonContent.Create(new SubtitlesDeleteBody(videoMediaId, languageCode, SubtitleCategoryName(category)), Json.SubtitlesDeleteBody));
        return transport.SendAsync(request, Json.EnvelopeXMediaSubtitlesDeleteResult, cancellationToken);
    }

    public Task<XResponse<XMedia>> GetAsync(string mediaKey, XFieldSelection? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/media/{media_key}", XAuthMethods.Any, ("media_key", mediaKey)).Add(fields),
            Json.EnvelopeXMedia,
            cancellationToken);

    public Task<XResponse<IReadOnlyList<XMedia>>> GetManyAsync(IReadOnlyList<string> mediaKeys, XFieldSelection? fields = null, CancellationToken cancellationToken = default)
    {
        RequireKeys(mediaKeys);
        return transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/media", XAuthMethods.Any).Add("media_keys", mediaKeys).Add(fields),
            Json.EnvelopeXMediaList,
            cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XMediaAnalytics>>> GetAnalyticsAsync(IReadOnlyList<string> mediaKeys, DateTimeOffset startTime, DateTimeOffset endTime, string? granularity = null, IReadOnlyList<string>? metrics = null, CancellationToken cancellationToken = default)
    {
        RequireKeys(mediaKeys);
        var request = XApiRequest.Create(HttpMethod.Get, "/2/media/analytics", XAuthMethods.UserContext)
            .Add("media_keys", mediaKeys)
            .Add("start_time", startTime)
            .Add("end_time", endTime)
            .Add("granularity", granularity)
            .Add("media_analytics.fields", metrics);
        return transport.SendAsync(request, Json.EnvelopeXMediaAnalyticsList, cancellationToken);
    }

    private static void RequireKeys(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("Provide at least one media key.", nameof(keys));
        }
    }

    private static string SubtitleCategoryName(XSubtitleCategory category) => category switch
    {
        XSubtitleCategory.TweetVideo => "TweetVideo",
        XSubtitleCategory.AmplifyVideo => "AmplifyVideo",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    private async Task<XMediaUploadResult> UploadSimpleAsync(Stream content, XMediaUploadOptions options, string contentType, long limit, CancellationToken cancellationToken)
    {
        // Images are small (at most 5 MB), so buffering them is bounded and lets the request be rebuilt after a 429. The limit is enforced while
        // reading, so a huge or endless stream is rejected after limit + 1 bytes instead of being read into memory first.
        if (content.CanSeek && content.Length - content.Position > limit)
        {
            throw new ArgumentException($"The image is larger than the {limit}-byte limit.", nameof(content));
        }

        var buffer = new MemoryStream();
        await using (buffer.ConfigureAwait(false))
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > limit)
                {
                    throw new ArgumentException($"The image is larger than the {limit}-byte limit.", nameof(content));
                }

                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length == 0)
            {
                throw new ArgumentException("The stream is empty.", nameof(content));
            }

            var bytes = buffer.ToArray();
            var request = XApiRequest.Create(HttpMethod.Post, "/2/media/upload", XAuthMethods.UserContext).WithContent(() =>
            {
                var form = new MultipartFormDataContent();
                var media = new ByteArrayContent(bytes);
                media.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                form.Add(media, "media", options.FileName ?? "media");
                form.Add(new StringContent(CategoryName(options.Category)), "media_category");
                if (options.AdditionalOwners is { Count: > 0 } owners)
                {
                    form.Add(new StringContent(string.Join(',', owners)), "additional_owners");
                }

                return form;
            });

            var response = await transport.SendAsync(request, Json.EnvelopeXMediaUploadResult, cancellationToken).ConfigureAwait(false);
            options.Progress?.Report(new XUploadProgress(bytes.Length, bytes.Length));
            return Unwrap(response);
        }
    }

    private async Task<XMediaUploadResult> UploadChunkedAsync(Stream content, XMediaUploadOptions options, string contentType, long? length, CancellationToken cancellationToken)
    {
        if (length is null)
        {
            throw new ArgumentException("Chunked uploads (GIF and video) need a seekable stream so the total size is known.", nameof(content));
        }

        var total = length.Value;
        if (total <= 0)
        {
            throw new ArgumentException("The stream is empty.", nameof(content));
        }

        var segmentCount = (total + options.SegmentSize - 1) / options.SegmentSize;
        if (segmentCount > MaxSegments)
        {
            throw new ArgumentException($"The file needs {segmentCount} segments, above the maximum of {MaxSegments}. Increase SegmentSize (up to 5 MB).", nameof(options));
        }

        var init = XApiRequest.Create(HttpMethod.Post, "/2/media/upload/initialize", XAuthMethods.UserContext)
            .WithContent(() => JsonContent.Create(
                new InitBody(contentType, total, CategoryName(options.Category), options.AdditionalOwners),
                Json.InitBody));
        var started = Unwrap(await transport.SendAsync(init, Json.EnvelopeXMediaUploadResult, cancellationToken).ConfigureAwait(false));
        var mediaId = started.MediaId;

        var buffer = new byte[options.SegmentSize];
        long sent = 0;
        options.Progress?.Report(new XUploadProgress(0, total));
        for (var index = 0; sent < total; index++)
        {
            var count = await FillAsync(content, buffer, (int)Math.Min(buffer.Length, total - sent), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                throw new ArgumentException("The stream ended before the declared length was read.", nameof(content));
            }

            var segment = index;
            var append = XApiRequest.Create(HttpMethod.Post, "/2/media/upload/{id}/append", XAuthMethods.UserContext, ("id", mediaId)).WithContent(() =>
            {
                var form = new MultipartFormDataContent();
                form.Add(new StringContent(segment.ToString(CultureInfo.InvariantCulture)), "segment_index");
                var part = new ByteArrayContent(buffer, 0, count);
                part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                form.Add(part, "media", options.FileName ?? "media");
                return form;
            });

            var appended = await transport.SendAsync(append, Json.EnvelopeXMediaAppendData, cancellationToken).ConfigureAwait(false);
            if (!appended.IsSuccess)
            {
                throw new XApiException(appended.Error!);
            }

            sent += count;
            options.Progress?.Report(new XUploadProgress(sent, total));
        }

        // The stream must hold exactly the declared number of bytes; extra data means the file changed while uploading, so do not finalize a truncated copy.
        if (await content.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) > 0)
        {
            throw new ArgumentException("The stream is longer than the length declared at the start of the upload (it changed while uploading).", nameof(content));
        }

        var finalize = XApiRequest.Create(HttpMethod.Post, "/2/media/upload/{id}/finalize", XAuthMethods.UserContext, ("id", mediaId));
        return Unwrap(await transport.SendAsync(finalize, Json.EnvelopeXMediaUploadResult, cancellationToken).ConfigureAwait(false));
    }

    private async Task<XMediaUploadResult> PollAsync(string mediaId, TimeSpan timeout, XMediaProcessingInfo? initial, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        var deadline = transport.TimeProvider.GetUtcNow() + timeout;
        var info = initial;
        XMediaUploadResult? last = null;

        while (true)
        {
            if (info is not null)
            {
                if (info.IsFailed)
                {
                    throw new XMediaProcessingException($"X failed to process media {mediaId}.", mediaId, info);
                }

                if (info.IsSucceeded)
                {
                    return last ?? new XMediaUploadResult { MediaId = mediaId, ProcessingInfo = info };
                }

                var wait = TimeSpan.FromSeconds(Math.Clamp(info.CheckAfterSecs ?? 1, 1, 30));
                if (transport.TimeProvider.GetUtcNow() + wait > deadline)
                {
                    throw new XMediaProcessingException($"Media {mediaId} was still processing after {timeout}.", mediaId, info);
                }

                await Task.Delay(wait, transport.TimeProvider, cancellationToken).ConfigureAwait(false);
            }

            var status = await GetUploadStatusAsync(mediaId, cancellationToken).ConfigureAwait(false);
            last = Unwrap(status);
            info = last.ProcessingInfo;

            // No processing_info means there is nothing left to wait for.
            if (info is null)
            {
                return last;
            }
        }
    }

    private static async Task<int> FillAsync(Stream stream, byte[] buffer, int count, CancellationToken cancellationToken)
    {
        var filled = 0;
        while (filled < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled, count - filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }

    private static XMediaUploadResult Unwrap(XResponse<XMediaUploadResult> response) =>
        response.IsSuccess ? response.GetDataOrThrow() : throw new XApiException(response.Error!);

    internal static string? InferContentType(string? fileName) =>
        Path.GetExtension(fileName)?.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".ts" => "video/mp2t",
            ".srt" => "text/srt",
            ".vtt" => "text/vtt",
            _ => null,
        };

    internal static void ValidateContentType(XMediaCategory category, string contentType)
    {
        string[] allowed = category switch
        {
            XMediaCategory.TweetImage or XMediaCategory.DmImage => ["image/jpeg", "image/pjpeg", "image/png", "image/webp", "image/bmp", "image/tiff"],
            XMediaCategory.TweetGif or XMediaCategory.DmGif => ["image/gif"],
            XMediaCategory.Subtitles => ["text/srt", "text/vtt"],
            _ => ["video/mp4", "video/quicktime", "video/webm", "video/mp2t"],
        };

        if (!allowed.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{contentType}' is not supported for {CategoryName(category)}. Allowed: {string.Join(", ", allowed)}.", nameof(contentType));
        }
    }

    /// <summary>
    /// The largest size X documents per category. Video limits are the Premium maxima (16 GB for Post videos, 1 GB for DM videos);
    /// accounts without Premium have lower caps that X enforces itself.
    /// </summary>
    internal static long MaxBytes(XMediaCategory category) => category switch
    {
        XMediaCategory.TweetImage or XMediaCategory.DmImage => 5 * MiB,
        XMediaCategory.TweetGif or XMediaCategory.DmGif => 15 * MiB,
        XMediaCategory.Subtitles => MiB,
        XMediaCategory.DmVideo => 1024 * MiB,
        _ => 16L * 1024 * MiB,
    };

    internal static string CategoryName(XMediaCategory category) => category switch
    {
        XMediaCategory.TweetImage => "tweet_image",
        XMediaCategory.TweetGif => "tweet_gif",
        XMediaCategory.TweetVideo => "tweet_video",
        XMediaCategory.DmImage => "dm_image",
        XMediaCategory.DmGif => "dm_gif",
        XMediaCategory.DmVideo => "dm_video",
        XMediaCategory.Subtitles => "subtitles",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    internal sealed record InitBody(string MediaType, long TotalBytes, string MediaCategory, IReadOnlyList<string>? AdditionalOwners);

    internal sealed record MetadataBody(string Id, MetadataPayload Metadata);

    internal sealed record MetadataPayload(
        AltTextPayload? AltText,
        AllowDownloadPayload? AllowDownloadStatus,
        ExpirationPayload? ContentExpiration,
        DomainPayload? DomainRestrictions,
        OriginPayload? FoundMediaOrigin,
        ManagedPayload? ManagementInfo,
        SharedPayload? SharedInfo,
        UploadSourcePayload? UploadSource);

    internal sealed record AltTextPayload(string Text);

    internal sealed record AllowDownloadPayload(bool AllowDownload);

    internal sealed record ExpirationPayload(double TimestampSec);

    internal sealed record DomainPayload(IReadOnlyList<string> Whitelist);

    internal sealed record OriginPayload(string Provider, string Id);

    internal sealed record ManagedPayload(bool Managed);

    internal sealed record SharedPayload(bool Shared);

    internal sealed record UploadSourcePayload(string UploadSource);

    internal sealed record SubtitlesBody(string Id, string MediaCategory, SubtitlePayload Subtitles);

    internal sealed record SubtitlePayload(string DisplayName, string Id, string LanguageCode);

    internal sealed record SubtitlesDeleteBody(string Id, string LanguageCode, string MediaCategory);
}
