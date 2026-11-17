using System.Net;
using System.Text;
using AwesomeAssertions;
using XConnect.Streaming;

namespace XConnect.Tests;

/// <summary>Content whose stream never ends. It counts the bytes handed out so a test can prove reading stopped near the limit.</summary>
internal sealed class EndlessContent : HttpContent
{
    public long BytesServed;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new Endless(this));

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    private sealed class Endless(EndlessContent owner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            buffer.Span.Fill((byte)'a');
            Interlocked.Add(ref owner.BytesServed, buffer.Length);
            return ValueTask.FromResult(buffer.Length);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>Content that reports a length and fails if anyone reads it.</summary>
internal sealed class DeclaredButUnreadContent(long length) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

    protected override Task<Stream> CreateContentReadStreamAsync() => throw new InvalidOperationException("The body must not be read when its declared length is over the limit.");

    protected override bool TryComputeLength(out long l)
    {
        l = length;
        return true;
    }
}

internal sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return respond(request, cancellationToken);
    }
}

public sealed class ResponseBodyLimitTests
{
    private static XConnectClient Client(HttpMessageHandler handler, long limit)
    {
        var options = new XConnectOptions { MaxResponseBodyBytes = limit }.UseBearerToken("app");
        return new XConnectClient(new HttpClient(handler), options, new InstantTimeProvider());
    }

    private static HttpResponseMessage Chunked(HttpStatusCode status, byte[] bytes)
    {
        // A stream with no known length models chunked transfer: the size is unknown until it has been read.
        var content = new StreamContent(new NoLengthStream(bytes));
        content.Headers.ContentType = new("application/json");
        return new HttpResponseMessage(status) { Content = content };
    }

    private sealed class NoLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }

    private const string UserJson = """{"data":{"id":"1","name":"n","username":"u"}}""";

    [Fact]
    public async Task BodyExactlyAtTheLimit_IsAccepted_AndOneByteOverIsRejected()
    {
        var bytes = Encoding.UTF8.GetBytes(UserJson);
        var ok = await Client(new DelegateHandler((_, _) => Task.FromResult(Chunked(HttpStatusCode.OK, bytes))), bytes.Length).Users.GetByIdAsync("1");
        ok.Data!.Id.Should().Be("1");

        var act = () => Client(new DelegateHandler((_, _) => Task.FromResult(Chunked(HttpStatusCode.OK, bytes))), bytes.Length - 1).Users.GetByIdAsync("1");
        var thrown = await act.Should().ThrowAsync<XResponseTooLargeException>();
        thrown.Which.LimitBytes.Should().Be(bytes.Length - 1);
    }

    [Fact]
    public async Task OversizedJson_IsNeverTruncatedAndTreatedAsValid()
    {
        // The first N bytes of this body are a complete JSON object: a truncating reader would return a valid user.
        var bytes = Encoding.UTF8.GetBytes(UserJson + new string(' ', 4096));
        var act = () => Client(new DelegateHandler((_, _) => Task.FromResult(Chunked(HttpStatusCode.OK, bytes))), UserJson.Length).Users.GetByIdAsync("1");
        await act.Should().ThrowAsync<XResponseTooLargeException>();
    }

    [Fact]
    public async Task DeclaredLengthOverTheLimit_IsRefusedWithoutReadingTheBody()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new DeclaredButUnreadContent(10_000_000) }));
        var act = () => Client(handler, 1024).Users.GetByIdAsync("1");
        await act.Should().ThrowAsync<XResponseTooLargeException>();
    }

    [Fact(Timeout = 20_000)]
    public async Task AnEndlessBody_StopsNearTheLimit_AndIsNotRetried()
    {
        var content = new EndlessContent();
        var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var act = () => Client(handler, 100_000).Users.GetByIdAsync("1");

        await act.Should().ThrowAsync<XResponseTooLargeException>();
        content.BytesServed.Should().BeLessThan(100_000 + 16 * 1024 + 1, "reading must stop at the limit, not run on");
        handler.Calls.Should().Be(1, "the same response would exceed the limit again, so a GET is not retried");
    }

    [Fact]
    public async Task OversizedErrorBody_IsAlsoRefused()
    {
        var bytes = new byte[5000];
        Array.Fill(bytes, (byte)'x');
        var act = () => Client(new DelegateHandler((_, _) => Task.FromResult(Chunked(HttpStatusCode.BadGateway, bytes))), 1000).Users.GetByIdAsync("1");
        await act.Should().ThrowAsync<XResponseTooLargeException>();
    }

    [Fact]
    public async Task MultiByteCharactersAndABom_DecodeCorrectlyAcrossReads()
    {
        var json = "{\"data\":{\"id\":\"1\",\"name\":\"" + string.Concat(Enumerable.Repeat("日本語🙂", 3000)) + "\",\"username\":\"u\"}}";
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        var response = await Client(new DelegateHandler((_, _) => Task.FromResult(Chunked(HttpStatusCode.OK, bytes))), 1_000_000).Users.GetByIdAsync("1");
        response.Data!.Name.Should().StartWith("日本語🙂").And.HaveLength(3000 * "日本語🙂".Length);
    }

    [Fact]
    public void Options_RejectNonPositiveLimits()
    {
        new XConnectOptions { MaxResponseBodyBytes = 0 }.UseBearerToken("a").Validate().Should().Contain("MaxResponseBodyBytes");
        new XConnectOptions { TransferTimeout = TimeSpan.Zero }.UseBearerToken("a").Validate().Should().Contain("TransferTimeout");
        new XConnectOptions { TransferTimeout = Timeout.InfiniteTimeSpan }.UseBearerToken("a").Validate().Should().BeNull();
        new XConnectOptions().UseBearerToken("a").Validate().Should().BeNull();
    }
}

public sealed class StreamLineLimitTests
{
    private static readonly XConnectOptions AppOnly = new XConnectOptions().UseBearerToken("app");

    private static string Line(string id, int padding = 0) => "{\"data\":{\"id\":\"" + id + "\",\"text\":\"" + new string('t', padding) + "\"}}";

    private static HttpResponseMessage Stream(string body) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body))) };

    private static async Task<List<XStreamEvent>> Take(string body, int count, XStreamOptions options)
    {
        var handler = new QueueHandler().Enqueue(_ => Stream(body));
        var client = TestClients.Create(handler, AppOnly);
        var events = new List<XStreamEvent>();
        await foreach (var e in client.Streaming.StreamSampleAsync(options, CancellationToken.None))
        {
            events.Add(e);
            if (events.Count == count)
            {
                break;
            }
        }

        return events;
    }

    [Fact]
    public async Task LineExactlyAtTheLimit_IsDelivered()
    {
        var line = Line("1", 50);
        // The limit counts the line's bytes before the "\n", so the "\r" of a "\r\n" terminator counts too.
        var events = await Take(line + "\r\n" + Line("2") + "\r\n", 2, new XStreamOptions { MaxLineBytes = line.Length + 1 });
        events.Select(e => e.Post?.Id).Should().Equal("1", "2");

        var over = await Take(line + "\r\n" + Line("2") + "\r\n", 2, new XStreamOptions { MaxLineBytes = line.Length });
        over[0].Errors!.Single().Title.Should().Be("Stream line too large");
        over[1].Post!.Id.Should().Be("2");
    }

    [Fact]
    public async Task OversizedLine_IsSkipped_ReportedInBand_AndTheNextLinesStayIntact()
    {
        // 30,000 bytes spans several internal read chunks, so the discard path crosses buffer boundaries.
        var big = Line("big", 30_000);
        var events = await Take(Line("1") + "\r\n" + big + "\r\n" + Line("3") + "\r\n", 3, new XStreamOptions { MaxLineBytes = 1000 });

        events[0].Post!.Id.Should().Be("1");
        events[1].Post.Should().BeNull();
        events[1].Errors!.Single().Title.Should().Be("Stream line too large");
        events[2].Post!.Id.Should().Be("3", "the line after an oversized one must parse normally");
    }

    [Fact]
    public async Task LongLineUnderTheLimit_AcrossManyReadChunks_IsDeliveredWhole()
    {
        var events = await Take(Line("1", 40_000) + "\r\n", 1, new XStreamOptions { MaxLineBytes = 100_000 });
        events.Single().Post!.Text.Should().HaveLength(40_000);
    }

    [Fact]
    public async Task OversizedLineWithoutANewline_BeforeTheStreamEnds_IsSkippedWithoutBufferingIt()
    {
        var handler = new QueueHandler().Enqueue(_ => Stream(Line("1") + "\n" + new string('x', 200_000)));
        var client = TestClients.Create(handler, AppOnly);
        await using var enumerator = client.Streaming.StreamSampleAsync(new XStreamOptions { MaxLineBytes = 1000, MaxReconnects = 0 }, CancellationToken.None).GetAsyncEnumerator();

        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Post!.Id.Should().Be("1");
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Errors!.Single().Title.Should().Be("Stream line too large");
    }

    [Fact]
    public async Task KeepAliveBlankLines_StillWork()
    {
        var events = await Take("\r\n\r\n" + Line("7") + "\r\n", 1, new XStreamOptions { MaxLineBytes = 500 });
        events.Single().Post!.Id.Should().Be("7");
    }

    [Fact]
    public async Task NonPositiveMaxLineBytes_IsRejected()
    {
        var client = TestClients.Create(new QueueHandler(), AppOnly);
        var act = async () =>
        {
            await foreach (var _ in client.Streaming.StreamSampleAsync(new XStreamOptions { MaxLineBytes = 0 }, CancellationToken.None))
            {
            }
        };
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task OversizedStreamErrorBody_StillFailsWithTheStatusClassification()
    {
        var options = new XConnectOptions { MaxResponseBodyBytes = 100 }.UseBearerToken("app");
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Forbidden, new string('x', 5000));
        var client = new XConnectClient(new HttpClient(handler), options, new InstantTimeProvider());

        var act = async () =>
        {
            await foreach (var _ in client.Streaming.StreamSampleAsync(cancellationToken: CancellationToken.None))
            {
            }
        };

        (await act.Should().ThrowAsync<XApiException>()).Which.Error.Kind.Should().Be(XApiErrorKind.Authorization);
    }
}

public sealed class PresignedTransferTimeoutTests
{
    /// <summary>Upper bound in milliseconds for every test here, so a regression that removes a timeout fails fast instead of hanging the run.</summary>
    private const int Watchdog = 20_000;

    private static readonly Uri Upload = new("https://storage.example.test/upload?sig=a");
    private static readonly Uri Download = new("https://storage.example.test/download?sig=b");

    private static XConnectClient Client(HttpMessageHandler handler, TimeSpan transfer) =>
        new(new HttpClient(handler), new XConnectOptions { TransferTimeout = transfer }.UseBearerToken("app"), new InstantTimeProvider());

    private static Task<HttpResponseMessage> Hang(HttpRequestMessage _, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith<HttpResponseMessage>(_ => throw new InvalidOperationException(), ct);

    private static Task<HttpResponseMessage> Ok(HttpRequestMessage _, CancellationToken __) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"1\"}\n") });

    [Fact(Timeout = Watchdog)]
    public async Task Upload_Succeeds_WithinTheLimit()
    {
        var client = Client(new DelegateHandler(Ok), TimeSpan.FromMinutes(1));
        await using var ids = new MemoryStream([1, 2, 3]);
        await client.Compliance.UploadIdsAsync(Upload, ids);
    }

    [Fact(Timeout = Watchdog)]
    public async Task Upload_ThatHangs_ThrowsTimeoutException()
    {
        var client = Client(new DelegateHandler(Hang), TimeSpan.FromMilliseconds(100));
        await using var ids = new MemoryStream([1]);
        var act = () => client.Compliance.UploadIdsAsync(Upload, ids);
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact(Timeout = Watchdog)]
    public async Task Upload_CallerCancellation_IsNotReportedAsATimeout()
    {
        var client = Client(new DelegateHandler(Hang), TimeSpan.FromMinutes(5));
        await using var ids = new MemoryStream([1]);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var act = () => client.Compliance.UploadIdsAsync(Upload, ids, cts.Token);
        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().NotBeOfType<TimeoutException>();
    }

    [Fact(Timeout = Watchdog)]
    public async Task Upload_DoesNotBufferTheResponseBody()
    {
        // An endless response body must not hang or exhaust memory: only the status is needed.
        var content = new EndlessContent();
        var client = Client(new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })), TimeSpan.FromSeconds(30));
        await using var ids = new MemoryStream([1]);
        await client.Compliance.UploadIdsAsync(Upload, ids);
        content.BytesServed.Should().Be(0);
    }

    [Fact(Timeout = Watchdog)]
    public async Task Download_Succeeds_AndStreamsTheResults()
    {
        var client = Client(new DelegateHandler(Ok), TimeSpan.FromMinutes(1));
        await using var stream = await client.Compliance.DownloadResultsAsync(Download);
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().Contain("\"id\":\"1\"");
    }

    [Fact(Timeout = Watchdog)]
    public async Task Download_ThatNeverStarts_ThrowsTimeoutException()
    {
        var client = Client(new DelegateHandler(Hang), TimeSpan.FromMilliseconds(100));
        var act = () => client.Compliance.DownloadResultsAsync(Download);
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact(Timeout = Watchdog)]
    public async Task Download_CallerCancellation_IsNotReportedAsATimeout()
    {
        var client = Client(new DelegateHandler(Hang), TimeSpan.FromMinutes(5));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var act = () => client.Compliance.DownloadResultsAsync(Download, cts.Token);
        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().NotBeOfType<TimeoutException>();
    }

    private sealed class StallingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new Stalling());

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        private sealed class Stalling : Stream
        {
            private bool _first = true;

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_first)
                {
                    _first = false;
                    buffer.Span[0] = (byte)'x';
                    return 1;
                }

                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    [Fact(Timeout = Watchdog)]
    public async Task Download_ThatStallsMidStream_ThrowsTimeoutException_ButProgressIsNotCutOff()
    {
        var client = Client(new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent() })), TimeSpan.FromMilliseconds(150));
        await using var stream = await client.Compliance.DownloadResultsAsync(Download);
        var buffer = new byte[16];

        (await stream.ReadAsync(buffer)).Should().Be(1, "the first read makes progress inside the limit");
        var act = async () => await stream.ReadAsync(buffer);
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact(Timeout = Watchdog)]
    public async Task Download_StallRead_CallerCancellation_IsNotReportedAsATimeout()
    {
        var client = Client(new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent() })), TimeSpan.FromMinutes(5));
        await using var stream = await client.Compliance.DownloadResultsAsync(Download);
        var buffer = new byte[16];
        (await stream.ReadAsync(buffer)).Should().Be(1);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await stream.ReadAsync(buffer, cts.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.Should().NotBeOfType<TimeoutException>();
    }

    [Fact(Timeout = Watchdog)]
    public async Task OrdinaryApiRequests_AreNotAffectedByTheTransferTimeout()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":{"id":"1","name":"n","username":"u"}}""") }));
        var response = await Client(handler, TimeSpan.FromTicks(1)).Users.GetByIdAsync("1");
        response.Data!.Id.Should().Be("1");
    }

    [Fact(Timeout = Watchdog)]
    public async Task InfiniteTransferTimeout_DisablesTheLimit()
    {
        var client = Client(new DelegateHandler(Ok), Timeout.InfiniteTimeSpan);
        await using var stream = await client.Compliance.DownloadResultsAsync(Download);
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().NotBeEmpty();
    }
}
