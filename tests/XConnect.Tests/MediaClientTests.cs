using System.Net;
using System.Text;
using AwesomeAssertions;
using XConnect.Models;

namespace XConnect.Tests;

public sealed class MediaClientTests
{
    private static MemoryStream Bytes(int count, byte fill = 7) => new(Enumerable.Repeat(fill, count).ToArray());

    [Fact]
    public async Task UploadImage_UsesASingleMultipartRequest()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"710511363345354753","media_key":"3_710511363345354753","size":1024}}""");
        var client = TestClients.Create(handler);
        await using var image = Bytes(1024);

        var result = await client.Media.UploadImageAsync(image, "announcement.png", CancellationToken.None);

        result.MediaId.Should().Be("710511363345354753");
        result.MediaKey.Should().Be("3_710511363345354753");
        var request = handler.Requests.Single();
        request.Uri.Should().Be(new Uri("https://api.x.com/2/media/upload"));
        request.Method.Should().Be(HttpMethod.Post);
        request.Message.Content!.Headers.ContentType!.MediaType.Should().Be("multipart/form-data");
        var body = Encoding.Latin1.GetString(request.BodyBytes!);
        body.Should().Contain("name=media").And.Contain("tweet_image").And.Contain("Content-Type: image/png").And.Contain("announcement.png");
    }

    [Fact]
    public async Task UploadImage_ReportsProgress()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"1"}}""");
        var client = TestClients.Create(handler);
        var reports = new List<XUploadProgress>();
        await using var image = Bytes(500);

        await client.Media.UploadAsync(image, new XMediaUploadOptions { FileName = "a.jpg", Progress = new Progress<XUploadProgress>(reports.Add) }, CancellationToken.None);
        await Task.Delay(50);

        reports.Should().ContainSingle().Which.Should().Be(new XUploadProgress(500, 500));
    }

    [Fact]
    public async Task UploadImage_WorksWithNonSeekableStreams()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"1"}}""");
        var client = TestClients.Create(handler);
        await using var image = new NonSeekableStream(Bytes(2048));

        var result = await client.Media.UploadImageAsync(image, "a.jpg", CancellationToken.None);

        result.MediaId.Should().Be("1");
    }

    [Theory]
    [InlineData("clip.exe")]
    [InlineData("noextension")]
    public async Task Upload_UnknownContentType_IsRejected(string fileName)
    {
        var client = TestClients.Create(new QueueHandler());
        await using var data = Bytes(10);

        var act = () => client.Media.UploadImageAsync(data, fileName, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*ContentType*");
    }

    [Theory]
    [InlineData(XMediaCategory.TweetImage, "image/gif")]
    [InlineData(XMediaCategory.TweetImage, "video/mp4")]
    [InlineData(XMediaCategory.TweetGif, "image/png")]
    [InlineData(XMediaCategory.TweetVideo, "image/png")]
    [InlineData(XMediaCategory.DmVideo, "image/jpeg")]
    public async Task Upload_ContentTypeMustMatchTheCategory(XMediaCategory category, string contentType)
    {
        var handler = new QueueHandler();
        var client = TestClients.Create(handler);
        await using var data = Bytes(10);

        var act = () => client.Media.UploadAsync(data, new XMediaUploadOptions { Category = category, ContentType = contentType }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not supported*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_ImageOverTheDocumentedLimit_IsRejectedBeforeSending()
    {
        var handler = new QueueHandler();
        var client = TestClients.Create(handler);
        await using var tooBig = Bytes((5 * 1024 * 1024) + 1);

        var act = () => client.Media.UploadImageAsync(tooBig, "huge.png", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*limit*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_EmptyStream_IsRejected()
    {
        var client = TestClients.Create(new QueueHandler());
        await using var empty = new MemoryStream();

        var act = () => client.Media.UploadImageAsync(empty, "a.png", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*empty*");
    }

    [Fact]
    public async Task UploadVideo_UsesTheChunkedWorkflow_InSegments()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"99","media_key":"13_99","expires_after_secs":86400}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"expires_at":1}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"expires_at":1}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"expires_at":1}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"99","media_key":"13_99","size":10000}}""");
        var client = TestClients.Create(handler);
        var reports = new List<XUploadProgress>();
        await using var video = Bytes(10_000);

        var result = await client.Media.UploadAsync(
            video,
            new XMediaUploadOptions { Category = XMediaCategory.TweetVideo, FileName = "clip.mp4", SegmentSize = 4096, Progress = new SyncProgress(reports) },
            CancellationToken.None);

        result.MediaId.Should().Be("99");
        handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").Should().Equal(
            "POST /2/media/upload/initialize",
            "POST /2/media/upload/99/append",
            "POST /2/media/upload/99/append",
            "POST /2/media/upload/99/append",
            "POST /2/media/upload/99/finalize");
        handler.Requests[0].Body.Should().Be("""{"media_type":"video/mp4","total_bytes":10000,"media_category":"tweet_video"}""");

        var segments = handler.Requests.Skip(1).Take(3).Select(r => Encoding.Latin1.GetString(r.BodyBytes!)).ToList();
        for (var i = 0; i < 3; i++)
        {
            segments[i].Should().Contain("name=segment_index").And.Contain($"\r\n\r\n{i}\r\n");
        }

        // 4096 + 4096 + 1808 payload bytes plus multipart framing.
        handler.Requests[1].BodyBytes!.Length.Should().BeGreaterThan(4096);
        handler.Requests[3].BodyBytes!.Length.Should().BeLessThan(handler.Requests[1].BodyBytes!.Length);
        reports.Select(r => r.BytesSent).Should().Equal(0, 4096, 8192, 10_000);
        reports.Should().AllSatisfy(r => r.TotalBytes.Should().Be(10_000));
    }

    [Fact]
    public async Task UploadVideo_WaitsForAsynchronousProcessingBeforeReturning()
    {
        var time = new InstantTimeProvider();
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"5"}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"expires_at":1}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"5","processing_info":{"state":"pending","check_after_secs":2}}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"5","processing_info":{"state":"in_progress","check_after_secs":3,"progress_percent":40}}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"5","processing_info":{"state":"succeeded"}}}""");
        var client = TestClients.Create(handler, time: time);
        await using var video = Bytes(100);

        var result = await client.Media.UploadVideoAsync(video, "v.mp4", CancellationToken.None);

        result.ProcessingInfo!.IsSucceeded.Should().BeTrue();
        handler.Requests.Skip(3).Select(r => r.PathAndQuery).Should().Equal(
            "/2/media/upload?command=STATUS&media_id=5",
            "/2/media/upload?command=STATUS&media_id=5");
        time.Delays.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task UploadVideo_ProcessingFailure_ThrowsMediaProcessingException()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"5"}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"expires_at":1}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"5","processing_info":{"state":"pending","check_after_secs":1}}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"5","processing_info":{"state":"failed"}}}""");
        var client = TestClients.Create(handler);
        await using var video = Bytes(100);

        var act = () => client.Media.UploadVideoAsync(video, "v.mp4", CancellationToken.None);

        var exception = (await act.Should().ThrowAsync<XMediaProcessingException>()).Which;
        exception.MediaId.Should().Be("5");
        exception.ProcessingInfo!.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForProcessing_TimesOut_InsteadOfWaitingForever()
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, """{"data":{"id":"5","processing_info":{"state":"in_progress","check_after_secs":30}}}"""));
        var client = TestClients.Create(handler);

        var act = () => client.Media.WaitForProcessingAsync("5", TimeSpan.FromSeconds(10), CancellationToken.None);

        await act.Should().ThrowAsync<XMediaProcessingException>().WithMessage("*still processing*");
    }

    [Fact]
    public async Task WaitForProcessing_ReturnsImmediatelyWhenThereIsNothingToProcess()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"5"}}""");
        var client = TestClients.Create(handler);

        var result = await client.Media.WaitForProcessingAsync("5", cancellationToken: CancellationToken.None);

        result.MediaId.Should().Be("5");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task UploadVideo_RequiresASeekableStream()
    {
        var handler = new QueueHandler();
        var client = TestClients.Create(handler);
        await using var video = new NonSeekableStream(Bytes(100));

        var act = () => client.Media.UploadVideoAsync(video, "v.mp4", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*seekable*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadVideo_AppendFailure_ThrowsXApiException_AndStopsUploading()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"5"}}""")
            .Enqueue(HttpStatusCode.RequestEntityTooLarge, """{"title":"Payload Too Large","detail":"segment too big"}""");
        var client = TestClients.Create(handler);
        await using var video = Bytes(10_000);

        var act = () => client.Media.UploadAsync(video, new XMediaUploadOptions { Category = XMediaCategory.TweetVideo, FileName = "v.mp4", SegmentSize = 4096 }, CancellationToken.None);

        var error = (await act.Should().ThrowAsync<XApiException>()).Which.Error;
        error.Detail.Should().Be("segment too big");
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Upload_InitFailure_ThrowsXApiException()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Forbidden, """{"title":"Forbidden","detail":"nope"}""");
        var client = TestClients.Create(handler);
        await using var video = Bytes(100);

        var act = () => client.Media.UploadVideoAsync(video, "v.mp4", CancellationToken.None);

        (await act.Should().ThrowAsync<XApiException>()).Which.Error.Kind.Should().Be(XApiErrorKind.Authorization);
    }

    [Fact]
    public async Task Upload_Cancellation_StopsBetweenSegments()
    {
        using var cts = new CancellationTokenSource();
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, """{"data":{"id":"5","expires_at":1}}"""));
        var client = TestClients.Create(handler);
        await using var video = Bytes(100_000);
        var progress = new SyncProgress([], r =>
        {
            if (r.BytesSent >= 8192)
            {
                cts.Cancel();
            }
        });

        var act = () => client.Media.UploadAsync(video, new XMediaUploadOptions { Category = XMediaCategory.TweetVideo, FileName = "v.mp4", SegmentSize = 4096, Progress = progress }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Count.Should().BeLessThan(10);
    }

    [Fact]
    public async Task Upload_RejectsAnOversizedSegmentSize_AndTooManySegments()
    {
        var client = TestClients.Create(new QueueHandler());
        await using var video = Bytes(100);

        var tooBig = () => client.Media.UploadAsync(video, new XMediaUploadOptions { Category = XMediaCategory.TweetVideo, FileName = "v.mp4", SegmentSize = 6 * 1024 * 1024 }, CancellationToken.None);

        await tooBig.Should().ThrowAsync<ArgumentException>().WithMessage("*SegmentSize*");
        await using var large = new SparseStream(20_000);
        var act = () => client.Media.UploadAsync(large, new XMediaUploadOptions { Category = XMediaCategory.TweetVideo, FileName = "v.mp4", SegmentSize = 1 }, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*segments*");
    }

    [Fact]
    public async Task SetAltText_PostsMetadata()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"7","associated_metadata":{}}}""");
        var client = TestClients.Create(handler);

        var response = await client.Media.SetAltTextAsync("7", "A red bicycle", CancellationToken.None);

        response.Data!.Id.Should().Be("7");
        handler.Requests.Single().Body.Should().Be("""{"id":"7","metadata":{"alt_text":{"text":"A red bicycle"}}}""");
    }

    [Fact]
    public async Task MediaIdFromUpload_FlowsIntoAPost()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"710511363345354753"}}""")
            .Enqueue(HttpStatusCode.Created, """{"data":{"id":"1","text":"Our announcement"}}""");
        var client = TestClients.Create(handler);
        await using var image = Bytes(100);

        var media = await client.Media.UploadImageAsync(image, "announcement.png", CancellationToken.None);
        await client.Posts.CreateAsync(new CreatePostRequest { Text = "Our announcement", Media = new XPostMedia { MediaIds = [media.MediaId] } }, CancellationToken.None);

        handler.Requests[1].Body.Should().Be("""{"text":"Our announcement","media":{"media_ids":["710511363345354753"]}}""");
    }

    private sealed class SyncProgress(List<XUploadProgress> sink, Action<XUploadProgress>? also = null) : IProgress<XUploadProgress>
    {
        public void Report(XUploadProgress value)
        {
            sink.Add(value);
            also?.Invoke(value);
        }
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A seekable stream of zeros that holds no memory, to test size arithmetic.</summary>
    private sealed class SparseStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get => _position; set => _position = value; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, n);
            _position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
