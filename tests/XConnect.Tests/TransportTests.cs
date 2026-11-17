using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using XConnect.Authentication;
using XConnect.Models;

namespace XConnect.Tests;

public sealed class TransportTests
{
    private const string PostJson = """{"data":{"id":"1","text":"x"}}""";
    private static readonly CreatePostRequest NewPost = new() { Text = "x" };

    [Fact]
    public async Task Get_Transient503_IsRetriedThenSucceeds()
    {
        var time = new InstantTimeProvider();
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable, """{"title":"down"}""")
            .Enqueue(HttpStatusCode.ServiceUnavailable, """{"title":"down"}""")
            .Enqueue(HttpStatusCode.OK, PostJson);
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.Retry = new XRetryOptions { Jitter = 0 }), time);

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(3);
        time.Delays.Should().Equal(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1000));
    }

    [Fact]
    public async Task Get_RetriesStopAtMaxRetries_AndReturnTheLastResponse()
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.BadGateway, """{"title":"bad gateway"}"""));
        var client = TestClients.Create(handler);

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        handler.Requests.Should().HaveCount(3, "one attempt plus the default two retries");
        response.Error!.Kind.Should().Be(XApiErrorKind.Server);
    }

    [Fact]
    public async Task Post_5xx_IsNeverRetried_BecauseTheOutcomeIsAmbiguous()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.InternalServerError, """{"title":"oops"}""");
        var client = TestClients.Create(handler);

        var response = await client.Posts.CreateAsync(NewPost, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        response.Error!.Kind.Should().Be(XApiErrorKind.Server);
    }

    [Fact]
    public async Task Post_NetworkFailure_IsNeverRetried_AndTheExceptionPropagates()
    {
        var handler = new QueueHandler().EnqueueThrow(new HttpRequestException("connection reset"));
        var client = TestClients.Create(handler);

        var act = () => client.Posts.CreateAsync(NewPost, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("connection reset");
        handler.Requests.Should().ContainSingle("the Post may have been created before the response was lost");
    }

    [Fact]
    public async Task Get_NetworkFailure_IsRetried()
    {
        var handler = new QueueHandler()
            .EnqueueThrow(new HttpRequestException("reset"))
            .Enqueue(HttpStatusCode.OK, PostJson);
        var client = TestClients.Create(handler);

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Delete_NetworkFailure_IsRetried_BecauseDeleteIsIdempotent()
    {
        var handler = new QueueHandler()
            .EnqueueThrow(new HttpRequestException("reset"))
            .Enqueue(HttpStatusCode.OK, """{"data":{"deleted":true}}""");
        var client = TestClients.Create(handler);

        var response = await client.Posts.DeleteAsync("1", CancellationToken.None);

        response.Data!.Deleted.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Post_429_IsRetried_HonouringRetryAfter()
    {
        var time = new InstantTimeProvider();
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.TooManyRequests, """{"title":"Too Many Requests"}""", r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7)))
            .Enqueue(HttpStatusCode.Created, PostJson);
        var client = TestClients.Create(handler, time: time);

        var response = await client.Posts.CreateAsync(NewPost, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(2, "429 means X rejected the request before processing it");
        time.Delays.Should().Equal(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task Get_429_WaitsUntilTheRateLimitReset()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new InstantTimeProvider(now);
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.TooManyRequests, "{}", r => AddRateHeaders(r, 15, 0, now.AddSeconds(12)))
            .Enqueue(HttpStatusCode.OK, PostJson);
        var client = TestClients.Create(handler, time: time);

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        time.Delays.Should().Equal(TimeSpan.FromSeconds(13));  // 12 s until the reset plus one second of margin
    }

    [Fact]
    public async Task RateLimitWaitLongerThanMaxDelay_IsNotWaitedFor()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new InstantTimeProvider(now);
        var handler = new QueueHandler().Enqueue(HttpStatusCode.TooManyRequests, """{"title":"limit"}""", r => AddRateHeaders(r, 15, 0, now.AddMinutes(15)));
        var client = TestClients.Create(handler, time: time);

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        time.Delays.Should().BeEmpty("the client never waits indefinitely");
        response.Error!.Kind.Should().Be(XApiErrorKind.RateLimited);
        response.RateLimit!.Remaining.Should().Be(0);
        response.RateLimit.ResetsAt.Should().Be(now.AddMinutes(15));
    }

    [Fact]
    public async Task Retries_CanBeDisabled()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.ServiceUnavailable, "{}");
        var options = TestClients.OAuth1Options().Also(o => o.Retry = new XRetryOptions { MaxRetries = 0 });
        var client = TestClients.Create(handler, options);

        await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancellation_DuringARetryWait_StopsPromptly()
    {
        using var cts = new CancellationTokenSource();
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));
        var time = new CancelOnDelayTimeProvider(cts);
        var client = TestClients.Create(handler, time: time);

        var act = () => client.Posts.GetAsync("1", cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Cancellation_BeforeSending_ThrowsAndSendsNothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new QueueHandler();
        var client = TestClients.Create(handler);

        var act = () => client.Posts.GetAsync("1", cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Timeout_IsReportedAsTimeoutException_AndNotRetriedForPost()
    {
        var slow = new SlowHandler();
        var options = TestClients.OAuth1Options().Also(o => o.Timeout = TimeSpan.FromMilliseconds(50));
        var client = new XConnectClient(new HttpClient(slow), options);

        var act = () => client.Posts.CreateAsync(NewPost, CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
        slow.Calls.Should().Be(1);
    }

    [Fact]
    public async Task OAuth1_IsResignedOnEveryAttempt()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable, "{}")
            .Enqueue(HttpStatusCode.OK, PostJson);
        var client = TestClients.Create(handler);

        await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        handler.Requests[0].Authorization.Should().NotBe(handler.Requests[1].Authorization, "each attempt needs a fresh nonce");
    }

    [Fact]
    public async Task OAuth1Header_UsesTimeProviderAndContainsExpectedParameters()
    {
        var time = new InstantTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, PostJson);
        var client = TestClients.Create(handler, time: time);

        await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        var header = handler.Requests.Single().Authorization!;
        header.Should().Contain("oauth_timestamp=\"1700000000\"").And.Contain("oauth_consumer_key=\"key\"").And.Contain("oauth_signature=");
        header.Should().NotContain("secret");
    }

    [Fact]
    public async Task AuthenticatorSelection_PrefersAConfiguredMethodThatTheEndpointAccepts()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, PostJson)
            .Enqueue(HttpStatusCode.Created, PostJson);
        var options = new XConnectOptions().UseBearerToken("app-token").UseOAuth1(TestClients.Credentials);
        var client = TestClients.Create(handler, options);

        await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);
        await client.Posts.CreateAsync(NewPost, CancellationToken.None);

        handler.Requests[0].Authorization.Should().Be("Bearer app-token");
        handler.Requests[1].Authorization.Should().StartWith("OAuth ");
    }

    [Fact]
    public async Task AppOnlyOnly_CannotCreatePosts_AndNothingIsSent()
    {
        var handler = new QueueHandler();
        var client = TestClients.Create(handler, new XConnectOptions().UseBearerToken("app-token"));

        var act = () => client.Posts.CreateAsync(NewPost, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*accepts*UserContext*AppOnly*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RateLimits_AreTrackedPerEndpoint()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, PostJson, r => AddRateHeaders(r, 300, 299, now.AddMinutes(15)))
            .Enqueue(HttpStatusCode.Created, PostJson, r => AddRateHeaders(r, 100, 90, now.AddHours(1)));
        var client = TestClients.Create(handler, time: new InstantTimeProvider(now));

        var get = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);
        await client.Posts.CreateAsync(NewPost, CancellationToken.None);

        get.RateLimit.Should().Be(new XRateLimit(300, 299, now.AddMinutes(15)));
        client.RateLimits.TryGet(HttpMethod.Get, "/2/tweets/{id}", out var getLimit).Should().BeTrue();
        getLimit!.Limit.Should().Be(300);
        client.RateLimits.TryGet(HttpMethod.Post, "/2/tweets", out var postLimit).Should().BeTrue();
        postLimit!.Remaining.Should().Be(90);
        client.RateLimits.TryGet(HttpMethod.Delete, "/2/tweets/{id}", out _).Should().BeFalse();
        client.RateLimits.Snapshot().Should().HaveCount(2);
    }

    [Fact]
    public async Task MissingRateLimitHeaders_YieldNull()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, PostJson);
        var client = TestClients.Create(handler);

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        response.RateLimit.Should().BeNull();
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        var act = () => new XConnectClient(new HttpClient(new QueueHandler()), new XConnectOptions());

        act.Should().Throw<ArgumentException>().WithMessage("*authentication*");
    }

    [Fact]
    public async Task BaseAddress_IsConfigurable_AndNormalised()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, PostJson);
        var options = TestClients.OAuth1Options().Also(o => o.BaseAddress = new Uri("https://proxy.example.test/x"));
        var client = TestClients.Create(handler, options);

        await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        handler.Requests.Single().Uri.Should().Be(new Uri("https://proxy.example.test/x/2/tweets/1"));
    }

    [Fact]
    public async Task PathParameters_AreEscaped()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","name":"n","username":"a b"}}""");
        var client = TestClients.Create(handler);

        await client.Users.GetByUsernameAsync("a/b c", cancellationToken: CancellationToken.None);

        handler.Requests.Single().Uri.AbsolutePath.Should().Be("/2/users/by/username/a%2Fb%20c");
    }

    [Fact]
    public async Task Logging_NeverIncludesSecrets()
    {
        var logger = new CapturingLogger();
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable, "{}")
            .Enqueue(HttpStatusCode.OK, PostJson);
        var client = new XConnectClient(new HttpClient(handler), TestClients.OAuth1Options(), new InstantTimeProvider(), logger);

        await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().AllSatisfy(m => m.Should().NotContain("token-secret").And.NotContain("OAuth ").And.NotContain("secret"));
    }

    private static void AddRateHeaders(HttpResponseMessage response, int limit, int remaining, DateTimeOffset reset)
    {
        response.Headers.Add("x-rate-limit-limit", limit.ToString());
        response.Headers.Add("x-rate-limit-remaining", remaining.ToString());
        response.Headers.Add("x-rate-limit-reset", reset.ToUnixTimeSeconds().ToString());
    }

    private sealed class CancelOnDelayTimeProvider(CancellationTokenSource cts) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            cts.Cancel();
            return base.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<XConnectClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : exception.ToString()));
    }
}
