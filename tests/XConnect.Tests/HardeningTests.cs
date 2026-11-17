using System.Net;
using System.Text;
using AwesomeAssertions;
using XConnect.Authentication;
using XConnect.Infrastructure;
using XConnect.Models;
using XConnect.Streaming;

namespace XConnect.Tests;

public sealed class JitterTests
{
    [Theory]
    [InlineData(0.0, 1000)]
    [InlineData(0.5, 900)]
    [InlineData(0.999, 800)]
    public void ApplyJitter_StaysWithinTheConfiguredBand_AndNeverExceedsTheInput(double random, int expectedMs)
    {
        var delay = XRetryPolicy.ApplyJitter(TimeSpan.FromSeconds(1), 0.2, () => random);

        delay.TotalMilliseconds.Should().BeApproximately(expectedMs, 1);
        delay.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ApplyJitter_WithZeroJitter_IsExact_AndOutOfRangeValuesAreClamped()
    {
        XRetryPolicy.ApplyJitter(TimeSpan.FromSeconds(3), 0, () => 0.9).Should().Be(TimeSpan.FromSeconds(3));
        XRetryPolicy.ApplyJitter(TimeSpan.FromSeconds(3), 5, () => 1).Should().Be(TimeSpan.Zero, "jitter is clamped to 1");
        XRetryPolicy.ApplyJitter(TimeSpan.FromSeconds(3), 0.2, () => -4).Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void ExtendWithJitter_NeverShortensAnInstructedWait()
    {
        XRetryPolicy.ExtendWithJitter(TimeSpan.FromMinutes(1), 0.2, () => 0).Should().Be(TimeSpan.FromMinutes(1));
        XRetryPolicy.ExtendWithJitter(TimeSpan.FromMinutes(1), 0.2, () => 1).Should().Be(TimeSpan.FromSeconds(72));
    }

    [Fact]
    public async Task SafeRetries_UseJitteredBackoff_ButStillHonourMaxDelay()
    {
        var time = new InstantTimeProvider();
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable, """{"title":"down"}""")
            .Enqueue(HttpStatusCode.ServiceUnavailable, """{"title":"down"}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","text":"t"}}""");
        var options = TestClients.OAuth1Options().Also(o => o.Retry = new XRetryOptions { Jitter = 0.5, RandomSource = () => 1.0, MaxDelay = TimeSpan.FromMilliseconds(600) });
        var client = TestClients.Create(handler, options, time);

        (await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None)).IsSuccess.Should().BeTrue();

        // 500 ms and 1000 ms capped to 600 ms, each scaled by 1 - 0.5.
        time.Delays.Should().Equal(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task PostCreation_IsNeverRetried_AfterAnAmbiguousFailure()
    {
        foreach (var failure in new Action<QueueHandler>[]
        {
            h => h.Enqueue(HttpStatusCode.ServiceUnavailable, """{"title":"down"}"""),
            h => h.Enqueue(HttpStatusCode.BadGateway, """{"title":"bad"}"""),
            h => h.EnqueueThrow(new HttpRequestException("connection reset")),
        })
        {
            var handler = new QueueHandler();
            failure(handler);
            handler.Enqueue(HttpStatusCode.Created, """{"data":{"id":"2","text":"dup"}}""");   // would become a duplicate Post if it were retried
            var client = TestClients.Create(handler);

            try
            {
                await client.Posts.CreateAsync(new CreatePostRequest { Text = "hello" }, CancellationToken.None);
            }
            catch (HttpRequestException)
            {
            }

            handler.Requests.Should().ContainSingle("a POST that fails ambiguously may already have been processed");
        }
    }
}

public sealed class StreamHardeningTests
{
    private static readonly XConnectOptions AppOnly = new XConnectOptions().UseBearerToken("app");

    private sealed class HangingHandler : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static async Task Drain(IAsyncEnumerable<XStreamEvent> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }

    [Fact]
    public async Task ConnectTimeout_TurnsASilentServerIntoAFailedAttempt_ThenBacksOff()
    {
        var handler = new HangingHandler();
        var time = new InstantTimeProvider();
        var client = new XConnectClient(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, AppOnly, time);
        var options = new XStreamOptions { ConnectTimeout = TimeSpan.FromMilliseconds(80), MaxReconnects = 1, Jitter = 0 };

        var act = () => Drain(client.Streaming.StreamSampleAsync(options, CancellationToken.None));

        await act.Should().ThrowAsync<TimeoutException>();
        handler.Calls.Should().Be(2, "one retry after the first timeout, then give up");
        time.Delays.Should().Equal(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CallerCancellation_DuringConnect_IsNotReportedAsATimeout()
    {
        var handler = new HangingHandler();
        var client = new XConnectClient(new HttpClient(handler), AppOnly, new InstantTimeProvider());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        var act = () => Drain(client.Streaming.StreamSampleAsync(new XStreamOptions { ConnectTimeout = TimeSpan.FromMinutes(5) }, cts.Token));

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().NotBeOfType<TimeoutException>();
        handler.Calls.Should().Be(1, "cancelling must not trigger a reconnect");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task NonPositiveTimeouts_AreRejected_InsteadOfLoopingReconnects(int seconds)
    {
        var client = TestClients.Create(new QueueHandler(), AppOnly);

        var keepAlive = () => Drain(client.Streaming.StreamSampleAsync(new XStreamOptions { KeepAliveTimeout = TimeSpan.FromSeconds(seconds) }, CancellationToken.None));
        var connect = () => Drain(client.Streaming.StreamSampleAsync(new XStreamOptions { ConnectTimeout = TimeSpan.FromSeconds(seconds) }, CancellationToken.None));

        await keepAlive.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await connect.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AServerThatOnlySendsKeepAlivesAndDrops_DoesNotCauseAReconnectStorm()
    {
        var time = new InstantTimeProvider();
        var handler = new QueueHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\r\n\r\n") });
        var client = TestClients.Create(handler, AppOnly, time);

        var act = () => Drain(client.Streaming.StreamSampleAsync(new XStreamOptions { MaxReconnects = 3, Jitter = 0 }, CancellationToken.None));

        await act.Should().ThrowAsync<IOException>();
        handler.Requests.Should().HaveCount(4);
        time.Delays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task ReconnectDelays_AreJitteredWithinTheBand_AndCapped()
    {
        var time = new InstantTimeProvider();
        var handler = new QueueHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) });
        var client = TestClients.Create(handler, AppOnly, time);
        var options = new XStreamOptions { MaxReconnects = 3, Jitter = 0.5, RandomSource = () => 1.0, MaxReconnectDelay = TimeSpan.FromSeconds(3) };

        var act = () => Drain(client.Streaming.StreamSampleAsync(options, CancellationToken.None));

        await act.Should().ThrowAsync<IOException>();
        time.Delays.Should().Equal(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5));
    }

    private sealed class TrackedContent(string text) : StringContent(text)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task AbandoningTheStream_ReleasesTheResponse()
    {
        var content = new TrackedContent("""{"data":{"id":"1","text":"a"}}""" + "\r\n" + """{"data":{"id":"2","text":"b"}}""" + "\r\n");
        var handler = new QueueHandler().Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var client = TestClients.Create(handler, AppOnly);

        await foreach (var e in client.Streaming.StreamSampleAsync(cancellationToken: CancellationToken.None))
        {
            e.Post!.Id.Should().Be("1");
            break;   // the consumer stops early
        }

        content.Disposed.Should().BeTrue("disposing the enumerator must dispose the response and its connection");
    }
}

public sealed class OAuth2CoordinationTests
{
    [Fact]
    public async Task ClientsSharingATokenStore_RefreshExactlyOnce_EvenWhenBuiltIndependently()
    {
        var store = new InMemoryXTokenStore(new XOAuth2Token { AccessToken = "old", RefreshToken = "r0", ExpiresAt = DateTimeOffset.UnixEpoch });
        var refreshes = 0;
        var handler = new QueueHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth2/token", StringComparison.Ordinal))
            {
                var n = Interlocked.Increment(ref refreshes);
                return QueueHandler.Json(HttpStatusCode.OK, $$"""{"access_token":"a{{n}}","refresh_token":"r{{n}}","expires_in":7200,"token_type":"bearer"}""");
            }

            return QueueHandler.Json(HttpStatusCode.OK, """{"data":{"id":"1","name":"n","username":"u"}}""");
        });

        // Separate XConnectClient instances (so separate authenticators), one shared store: the situation the review flagged.
        var clients = Enumerable.Range(0, 6)
            .Select(_ => new XConnectClient(new HttpClient(handler), new XConnectOptions { OAuth2 = new XOAuth2Options { ClientId = "c" }, TokenStore = store }, new InstantTimeProvider()))
            .ToList();

        var responses = await Task.WhenAll(clients.SelectMany(c => Enumerable.Range(0, 3).Select(_ => Task.Run(() => c.Users.GetMeAsync(cancellationToken: CancellationToken.None)))));

        responses.Should().OnlyContain(r => r.IsSuccess);
        refreshes.Should().Be(1, "a rotating refresh token must not be spent by two clients at once");
        (await store.GetTokenAsync(CancellationToken.None))!.RefreshToken.Should().Be("r1");
    }

    [Fact]
    public async Task ClientsWithDifferentStores_DoNotBlockEachOther()
    {
        var gate = new SemaphoreSlim(0);
        var storeA = new InMemoryXTokenStore(new XOAuth2Token { AccessToken = "oldA", RefreshToken = "rA", ExpiresAt = DateTimeOffset.UnixEpoch });
        var storeB = new InMemoryXTokenStore(new XOAuth2Token { AccessToken = "oldB", RefreshToken = "rB", ExpiresAt = DateTimeOffset.UnixEpoch });
        var handler = new QueueHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth2/token", StringComparison.Ordinal))
            {
                return QueueHandler.Json(HttpStatusCode.OK, """{"access_token":"new","refresh_token":"n","expires_in":7200}""");
            }

            return QueueHandler.Json(HttpStatusCode.OK, """{"data":{"id":"1","name":"n","username":"u"}}""");
        });
        XConnectClient Make(IXTokenStore s) => new(new HttpClient(handler), new XConnectOptions { OAuth2 = new XOAuth2Options { ClientId = "c" }, TokenStore = s }, new InstantTimeProvider());

        var results = await Task.WhenAll(Make(storeA).Users.GetMeAsync(cancellationToken: CancellationToken.None), Make(storeB).Users.GetMeAsync(cancellationToken: CancellationToken.None));

        results.Should().OnlyContain(r => r.IsSuccess);
        handler.Requests.Count(r => r.PathAndQuery.Contains("oauth2/token", StringComparison.Ordinal)).Should().Be(2, "each store refreshes its own token");
        gate.Dispose();
    }
}

public sealed class MediaMemoryTests
{
    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            buffer.Clear();
            BytesRead += buffer.Length;
            return buffer.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task SimpleUpload_FromAnEndlessStream_IsRejectedAfterTheLimit_NotBufferedWhole()
    {
        var handler = new QueueHandler();
        var client = TestClients.Create(handler);
        var endless = new EndlessStream();

        var act = () => client.Media.UploadImageAsync(endless, "huge.png", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*limit*");
        endless.BytesRead.Should().BeLessThan((5L * 1024 * 1024) + (2 * 81920), "reading stops one buffer past the 5 MB limit");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SimpleUpload_OfASeekableStreamOverTheLimit_IsRejectedWithoutReadingIt()
    {
        var client = TestClients.Create(new QueueHandler());
        await using var big = new MemoryStream(new byte[(5 * 1024 * 1024) + 1]);

        var act = () => client.Media.UploadImageAsync(big, "big.png", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*limit*");
        big.Position.Should().Be(0);
    }

    private sealed class GrowingStream(byte[] data, long declaredLength) : MemoryStream(data)
    {
        public override long Length => declaredLength;
    }

    [Fact]
    public async Task ChunkedUpload_ThatFindsMoreBytesThanDeclared_FailsBeforeFinalize()
    {
        var handler = new QueueHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/initialize", StringComparison.Ordinal)
            ? QueueHandler.Json(HttpStatusCode.OK, """{"data":{"id":"7","media_key":"13_7"}}""")
            : QueueHandler.Json(HttpStatusCode.OK, """{"data":{"expires_at":1}}"""));
        var client = TestClients.Create(handler);
        await using var video = new GrowingStream(new byte[10_000], declaredLength: 9_000);

        var act = () => client.Media.UploadAsync(video, new XMediaUploadOptions { Category = XMediaCategory.TweetVideo, FileName = "v.mp4", SegmentSize = 4096 }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*longer than*");
        handler.Requests.Select(r => r.PathAndQuery).Should().NotContain(p => p.EndsWith("/finalize", StringComparison.Ordinal));
    }
}
