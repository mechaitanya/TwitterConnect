# Media uploads

```csharp
await using var stream = File.OpenRead("announcement.png");
var media = await client.Media.UploadImageAsync(stream, "announcement.png");

await client.Posts.CreateAsync(new CreatePostRequest
{
    Text = "Our announcement",
    Media = new XPostMedia { MediaIds = [media.MediaId] },
});
```

Uploading needs user-context authentication (OAuth 1.0a, or OAuth 2.0 with `media.write`).

## The workflow is chosen for you

| Category (`XMediaCategory`) | Workflow | Documented limit |
| --- | --- | --- |
| `TweetImage`, `DmImage` | One multipart request to `POST /2/media/upload` | 5 MB |
| `TweetGif`, `DmGif` | Chunked: `initialize`, `append` × N, `finalize` | 15 MB |
| `TweetVideo`, `DmVideo` | Chunked, then status polling | Post video: 8 GB / 16 GB with Premium. DM video: 512 MB / 1 GB with Premium |

Image bodies are buffered (they are at most 5 MB); the limit is enforced while reading, so an oversized or endless stream is rejected after about 5 MB instead of being read into memory, and a seekable stream over the limit is rejected without being read. A chunked upload checks that the stream holds exactly the declared length and refuses to finalize if it grew while uploading. **GIFs and videos are streamed in segments**, 4 MB by default and at most 5 MB, so
nothing large is held in memory. Chunked uploads need a stream with a known length (`CanSeek`), because the total size is sent when the
upload starts. Up to 10 000 segments are allowed; the SDK checks this and the limits above before sending anything.

The SDK validates the content type against the category (for example `image/gif` is rejected for `TweetImage`) and infers it from
the file name, or you can set `XMediaUploadOptions.ContentType`. Video limits in the SDK are the Premium maxima; non-Premium
accounts have lower caps which X enforces itself (a too-long video is rejected with `403` when you create the Post).

## Options, progress and cancellation

```csharp
var result = await client.Media.UploadAsync(
    stream,
    new XMediaUploadOptions
    {
        Category = XMediaCategory.TweetVideo,
        FileName = "launch.mp4",
        SegmentSize = 4 * 1024 * 1024,
        ProcessingTimeout = TimeSpan.FromMinutes(5),
        Progress = new Progress<XUploadProgress>(p => Console.WriteLine($"{p.BytesSent}/{p.TotalBytes}")),
    },
    cancellationToken);
```

Cancellation is honoured between segments, during each request and while waiting for processing.

## Asynchronous processing

After `finalize`, X may process the media asynchronously (`processing_info` with `pending` or `in_progress`). `UploadAsync`
waits for it, polling `GET /2/media/upload?command=STATUS` at the interval X suggests (`check_after_secs`, clamped to 1-30 s), and
only returns once the media has **succeeded**. Create the Post after that. If processing fails you get an
`XMediaProcessingException`; if it exceeds `ProcessingTimeout` you get the same exception with a "still processing" message.
You can also poll yourself with `GetUploadStatusAsync` and `WaitForProcessingAsync(mediaId)`.

## Errors and retries

Uploads throw `XApiException` when X rejects a step (inspect `.Error`). Upload requests are not retried automatically, other than after
a `429`: X's chunked-upload documentation does not say whether repeating an `APPEND` is safe (see [verification-notes.md](verification-notes.md)). If an upload fails midway, start a new one.

## Metadata, alt text and tagging

```csharp
await client.Media.SetAltTextAsync(media.MediaId, "A red bicycle against a brick wall");   // 1-1000 characters, validated locally

await client.Media.SetMetadataAsync(media.MediaId, new XMediaMetadata
{
    AltText = "A red bicycle",
    AllowDownload = false,
    DomainWhitelist = ["example.com"],
});
// Tagging users in photos is part of the Post: XPostMedia.TaggedUserIds (up to 10)
```

Only the documented, typed properties are exposed (see [api-coverage.md](api-coverage.md#partially-supported-operations)). Metadata fields that the specification leaves untyped are not.

## Video details, call-to-action and subtitles

```csharp
await client.Posts.CreateAsync(new CreatePostRequest
{
    Text = "Watch",
    Media = new XPostMedia
    {
        MediaIds = [video.MediaId],
        Title = "Launch",
        CallToActions = new XPostCallToActions { VisitSite = new XUrlCallToAction { Url = "https://example.com" } },   // exactly one variant, HTTPS only
    },
});

// Subtitles: upload the .srt/.vtt (<= 1 MB), then attach it to the video
await using var srt = File.OpenRead("en.srt");
var subtitles = await client.Media.UploadAsync(srt, new XMediaUploadOptions { Category = XMediaCategory.Subtitles, FileName = "en.srt" });
await client.Media.CreateSubtitlesAsync(video.MediaId, XSubtitleCategory.TweetVideo, subtitles.MediaId, "en", "English");
```

## Lookup and analytics

`client.Media.GetAsync(mediaKey)` / `GetManyAsync(keys)` read media back (choose `XMediaFields`); `GetAnalyticsAsync(keys, start, end, "daily", ["video_views"])` returns video analytics, whose counters are `JsonElement`
because X returns either an integer or the original string.
