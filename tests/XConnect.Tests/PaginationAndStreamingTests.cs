using System.Net;
using System.Text;
using AwesomeAssertions;
using XConnect.Models;
using XConnect.Streaming;

namespace XConnect.Tests;

public sealed class PaginationTests
{
    private static string Page(string ids, string? next)
    {
        var items = ids.Split(',');
        var data = string.Join(',', items.Select(i => "{\"id\":\"" + i + "\",\"text\":\"t\"}"));
        var token = next is null ? string.Empty : ",\"next_token\":\"" + next + "\"";
        return "{\"data\":[" + data + "],\"meta\":{\"result_count\":" + items.Length + token + "}}";
    }

    [Fact]
    public async Task ItemsAsync_WalksAllPages_PassingTokensThrough()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, Page("1,2", "t2"))
            .Enqueue(HttpStatusCode.OK, Page("3,4", "t3"))
            .Enqueue(HttpStatusCode.OK, Page("5", null));
        var client = TestClients.Create(handler);

        var ids = new List<string?>();
        await foreach (var post in XPaginator.ItemsAsync((token, ct) =>
            client.Posts.GetUserPostsAsync("9", new XPostQuery { MaxResults = 5, PaginationToken = token }, ct)))
        {
            ids.Add(post.Id);
        }

        ids.Should().Equal("1", "2", "3", "4", "5");
        handler.Requests.Select(r => r.PathAndQuery).Should().Equal(
            "/2/users/9/tweets?max_results=5",
            "/2/users/9/tweets?max_results=5&pagination_token=t2",
            "/2/users/9/tweets?max_results=5&pagination_token=t3");
    }

    [Fact]
    public async Task PagesAsync_ExposesPerPageMetadata()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, Page("1,2", "t2")).Enqueue(HttpStatusCode.OK, Page("3", null));
        var client = TestClients.Create(handler);

        var pages = await XPaginator.PagesAsync((t, ct) => client.Posts.SearchRecentAsync("q", new XSearchQuery { PaginationToken = t }, ct)).ToListAsync();

        pages.Select(p => p.Meta!.ResultCount).Should().Equal(2, 1);
        pages[0].NextToken.Should().Be("t2");
        pages[1].NextToken.Should().BeNull();
    }

    [Fact]
    public async Task MaxPages_BoundsTheTraversal()
    {
        var calls = 0;
        Task<XResponse<IReadOnlyList<XPost>>> Fetch(string? token, CancellationToken ct)
        {
            calls++;
            return new QueueHandler().Enqueue(HttpStatusCode.OK, Page("1", "t" + calls)) is var h
                ? TestClients.Create(h).Posts.GetUserPostsAsync("1", null, ct)
                : throw new InvalidOperationException();
        }

        var pages = await XPaginator.PagesAsync<XPost>(Fetch, new XPaginationOptions { MaxPages = 3 }).ToListAsync();

        pages.Should().HaveCount(3);
        calls.Should().Be(3);
    }

    [Fact]
    public async Task MaxItems_StopsEarly()
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, Page("1,2,3", "next-" + Guid.NewGuid())));
        var client = TestClients.Create(handler);

        var items = await XPaginator.ItemsAsync(
            (t, ct) => client.Posts.GetUserPostsAsync("1", new XPostQuery { PaginationToken = t }, ct),
            new XPaginationOptions { MaxItems = 4 }).ToListAsync();

        items.Should().HaveCount(4);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task RepeatedCursor_EndsTheTraversal_InsteadOfLoopingForever()
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, Page("1", "same")));
        var client = TestClients.Create(handler);

        var pages = await XPaginator.PagesAsync(
            (t, ct) => client.Posts.GetUserPostsAsync("1", new XPostQuery { PaginationToken = t }, ct),
            new XPaginationOptions { MaxPages = 100 }).ToListAsync();

        pages.Should().HaveCount(2, "the first page returns 'same'; the second returns it again, which is detected");
    }

    [Fact]
    public async Task ErrorPage_IsYieldedLast_ByPagesAsync_AndThrownByItemsAsync()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, Page("1", "t2")).Enqueue(HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}""");
        var client = TestClients.Create(handler);

        var pages = await XPaginator.PagesAsync((t, ct) => client.Posts.GetUserPostsAsync("1", new XPostQuery { PaginationToken = t }, ct)).ToListAsync();
        pages.Last().Error!.Kind.Should().Be(XApiErrorKind.Authentication);

        var handler2 = new QueueHandler().Enqueue(HttpStatusCode.OK, Page("1", "t2")).Enqueue(HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}""");
        var client2 = TestClients.Create(handler2);
        var act = async () => await XPaginator.ItemsAsync((t, ct) => client2.Posts.GetUserPostsAsync("1", new XPostQuery { PaginationToken = t }, ct)).ToListAsync();
        await act.Should().ThrowAsync<XApiException>();
    }

    [Fact]
    public async Task Cancellation_StopsTraversal()
    {
        using var cts = new CancellationTokenSource();
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, Page("1", "n" + Guid.NewGuid())));
        var client = TestClients.Create(handler);
        var seen = 0;

        var act = async () =>
        {
            await foreach (var _ in XPaginator.ItemsAsync(
                (t, ct) => client.Posts.GetUserPostsAsync("1", new XPostQuery { PaginationToken = t }, ct),
                new XPaginationOptions { MaxPages = 50 },
                cts.Token))
            {
                if (++seen == 2)
                {
                    await cts.CancelAsync();
                }
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task InvalidMaxPages_IsRejected()
    {
        var act = async () => await XPaginator.PagesAsync<XPost>((_, _) => throw new InvalidOperationException(), new XPaginationOptions { MaxPages = 0 }).ToListAsync();

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}

public sealed class StreamingTests
{
    private static readonly XConnectOptions AppOnly = new XConnectOptions().UseBearerToken("app");

    private static string Line(string id, string rule = "r1") =>
        $$"""{"data":{"id":"{{id}}","text":"post {{id}}"},"matching_rules":[{"id":"{{rule}}","tag":"cats"}]}""";

    private static HttpResponseMessage StreamOf(params string[] lines) =>
        new(HttpStatusCode.OK) { Content = new StringContent(string.Join("\r\n", lines) + "\r\n", Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Filtered_YieldsPosts_SkipsKeepAlives_AndRulesMatch()
    {
        var handler = new QueueHandler()
            .Enqueue(StreamOf(Line("1"), string.Empty, Line("2"), "   ", Line("3")))
            .Enqueue(HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}""");
        var client = TestClients.Create(handler, AppOnly);

        var received = new List<XStreamEvent>();
        var act = async () =>
        {
            await foreach (var e in client.Streaming.StreamFilteredAsync(cancellationToken: CancellationToken.None))
            {
                received.Add(e);
            }
        };

        await act.Should().ThrowAsync<XApiException>("the reconnect is rejected with 401");
        received.Select(e => e.Post!.Id).Should().Equal("1", "2", "3");
        received[0].MatchingRules!.Single().Tag.Should().Be("cats");
        handler.Requests.First().PathAndQuery.Should().Be("/2/tweets/search/stream");
        handler.Requests.First().Authorization.Should().Be("Bearer app");
    }

    [Fact]
    public async Task Stream_ReconnectsAfterTheServerClosesTheConnection()
    {
        var time = new InstantTimeProvider();
        var handler = new QueueHandler()
            .Enqueue(StreamOf(Line("1")))
            .Enqueue(StreamOf(Line("2")));
        var client = TestClients.Create(handler, AppOnly, time);

        var received = new List<string?>();
        using var cts = new CancellationTokenSource();
        await foreach (var e in client.Streaming.StreamSampleAsync(new XStreamOptions { Jitter = 0 }, cts.Token))
        {
            received.Add(e.Post!.Id);
            if (received.Count == 2)
            {
                await cts.CancelAsync();
                break;
            }
        }

        received.Should().Equal("1", "2");
        handler.Requests.Should().HaveCount(2);
        time.Delays.Should().Equal(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Stream_RateLimited_WaitsAtLeastTheRateLimitDelay_ThenResumes()
    {
        var time = new InstantTimeProvider();
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.TooManyRequests, """{"title":"Too Many Requests"}""")
            .Enqueue(StreamOf(Line("1")));
        var client = TestClients.Create(handler, AppOnly, time);

        await using var enumerator = client.Streaming.StreamSampleAsync(cancellationToken: CancellationToken.None).GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();

        enumerator.Current.Post!.Id.Should().Be("1");
        time.Delays.Should().ContainSingle().Which.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Stream_GivesUpAfterMaxReconnects()
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.ServiceUnavailable, """{"title":"down"}"""));
        var client = TestClients.Create(handler, AppOnly);

        var act = async () =>
        {
            await foreach (var _ in client.Streaming.StreamSampleAsync(new XStreamOptions { MaxReconnects = 3 }, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<XApiException>();
        handler.Requests.Should().HaveCount(4, "the first attempt plus three reconnects");
    }

    [Fact]
    public async Task Stream_ReconnectsAfterNetworkFailure()
    {
        var handler = new QueueHandler().EnqueueThrow(new HttpRequestException("dns")).Enqueue(StreamOf(Line("1")));
        var client = TestClients.Create(handler, AppOnly);

        await using var enumerator = client.Streaming.StreamSampleAsync(cancellationToken: CancellationToken.None).GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();

        enumerator.Current.Post!.Id.Should().Be("1");
    }

    [Fact]
    public async Task Stream_SkipsMalformedLines_WithoutDroppingTheConnection()
    {
        var handler = new QueueHandler().Enqueue(StreamOf(Line("1"), "{broken json", Line("2")));
        var client = TestClients.Create(handler, AppOnly);

        await using var enumerator = client.Streaming.StreamSampleAsync(cancellationToken: CancellationToken.None).GetAsyncEnumerator();
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Post!.Id.Should().Be("1");
        (await enumerator.MoveNextAsync()).Should().BeTrue();
        enumerator.Current.Post!.Id.Should().Be("2");
    }

    [Fact]
    public async Task Stream_ForbiddenOrBadRequest_FailsFastWithoutReconnecting()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Forbidden, """{"title":"Forbidden","detail":"Your access level does not include this stream."}""");
        var client = TestClients.Create(handler, AppOnly);

        var act = async () =>
        {
            await foreach (var _ in client.Streaming.StreamFilteredAsync(cancellationToken: CancellationToken.None))
            {
            }
        };

        var exception = (await act.Should().ThrowAsync<XApiException>()).Which;
        exception.Error.Kind.Should().Be(XApiErrorKind.Authorization);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Stream_DeliversEventsAsTheyArrive_WithoutBufferingTheBody()
    {
        var gate = new TaskCompletionSource();
        var handler = new QueueHandler().Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new GatedContent(Line("1") + "\r\n", gate.Task, Line("2") + "\r\n") });
        var client = TestClients.Create(handler, AppOnly);

        await using var enumerator = client.Streaming.StreamSampleAsync(cancellationToken: CancellationToken.None).GetAsyncEnumerator();

        // The first event must be available while the second is still held back by the server.
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        enumerator.Current.Post!.Id.Should().Be("1");
        gate.SetResult();
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        enumerator.Current.Post!.Id.Should().Be("2");
    }

    [Fact]
    public async Task Stream_Timeout_TriggersAReconnect()
    {
        var handler = new QueueHandler()
            .Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new GatedContent(string.Empty, new TaskCompletionSource().Task, string.Empty) })
            .Enqueue(StreamOf(Line("9")));
        var client = TestClients.Create(handler, AppOnly);

        await using var enumerator = client.Streaming
            .StreamSampleAsync(new XStreamOptions { KeepAliveTimeout = TimeSpan.FromMilliseconds(100) }, CancellationToken.None)
            .GetAsyncEnumerator();
        (await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

        enumerator.Current.Post!.Id.Should().Be("9");
        handler.Requests.Should().HaveCount(2, "silence beyond the keep-alive window means the connection is dead");
    }

    [Fact]
    public async Task Stream_Options_AreSentAsQueryParameters()
    {
        var handler = new QueueHandler().Enqueue(StreamOf(Line("1")));
        var client = TestClients.Create(handler, AppOnly);

        await using var enumerator = client.Streaming.StreamFilteredAsync(
            new XStreamOptions { BackfillMinutes = 3, Fields = new XFieldSelection { PostFields = [XPostFields.CreatedAt] } },
            CancellationToken.None).GetAsyncEnumerator();
        await enumerator.MoveNextAsync();

        handler.Requests.First().PathAndQuery.Should().Be("/2/tweets/search/stream?tweet.fields=created_at&backfill_minutes=3");
    }

    [Fact]
    public async Task Rules_AddListAndDelete()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.Created, """{"data":[{"id":"1","value":"cats has:media","tag":"cats"}],"meta":{"sent":"2026-01-01T00:00:00.000Z","summary":{"created":1,"not_created":0,"valid":1,"invalid":0}}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"1","value":"cats has:media","tag":"cats"}],"meta":{"result_count":1}}""")
            .Enqueue(HttpStatusCode.OK, """{"meta":{"sent":"2026-01-01T00:00:00.000Z","summary":{"deleted":1,"not_deleted":0}}}""");
        var client = TestClients.Create(handler, AppOnly);

        var added = await client.Streaming.AddRulesAsync([new XStreamRuleDraft("cats has:media", "cats")], dryRun: true, CancellationToken.None);
        var rules = await client.Streaming.GetRulesAsync(cancellationToken: CancellationToken.None);
        var deleted = await client.Streaming.DeleteRulesAsync(["1"], cancellationToken: CancellationToken.None);

        added.Data!.Single().Id.Should().Be("1");
        added.Meta!.Summary!.Created.Should().Be(1);
        rules.Data!.Single().Tag.Should().Be("cats");
        deleted.Meta!.Summary!.Deleted.Should().Be(1);
        handler.Requests[0].PathAndQuery.Should().Be("/2/tweets/search/stream/rules?dry_run=true");
        handler.Requests[0].Body.Should().Be("""{"add":[{"value":"cats has:media","tag":"cats"}]}""");
        handler.Requests[2].Body.Should().Be("""{"delete":{"ids":["1"]}}""");
    }

    [Fact]
    public async Task Rules_Validation()
    {
        var client = TestClients.Create(new QueueHandler(), AppOnly);

        await client.Streaming.Invoking(s => s.AddRulesAsync([], false, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await client.Streaming.Invoking(s => s.AddRulesAsync([new XStreamRuleDraft(" ")], false, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await client.Streaming.Invoking(s => s.DeleteRulesAsync([], false, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Streaming_RequiresAnAppOnlyToken()
    {
        var client = TestClients.Create(new QueueHandler());

        var act = async () =>
        {
            await foreach (var _ in client.Streaming.StreamSampleAsync(cancellationToken: CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AppOnly*");
    }

    /// <summary>A network-like stream: serves the first chunk at once and the rest only after a gate opens.</summary>
    private sealed class GatedContent : StreamContent
    {
        public GatedContent(string first, Task gate, string rest)
            : base(new GatedStream(first, gate, rest))
        {
        }

        private sealed class GatedStream(string first, Task gate, string rest) : Stream
        {
            private byte[] _current = Encoding.UTF8.GetBytes(first);
            private int _offset;
            private bool _gatePassed;

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush()
            {
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_offset >= _current.Length)
                {
                    if (_gatePassed)
                    {
                        return 0;
                    }

                    await gate.WaitAsync(cancellationToken);
                    _gatePassed = true;
                    _current = Encoding.UTF8.GetBytes(rest);
                    _offset = 0;
                    if (_current.Length == 0)
                    {
                        return 0;
                    }
                }

                var n = Math.Min(buffer.Length, _current.Length - _offset);
                _current.AsMemory(_offset, n).CopyTo(buffer);
                _offset += n;
                return n;
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
