using System.Net;
using AwesomeAssertions;
using XConnect.Authentication;

namespace XConnect.Tests;

/// <summary>Regression tests for defects found by the independent review.</summary>
public sealed class ReviewFindingsTests
{
    [Fact]
    public void OAuth2Options_ToString_NeverPrintsTheClientSecret()
    {
        var text = new XOAuth2Options { ClientId = "cid", ClientSecret = "TOP-SECRET" }.ToString();

        text.Should().Contain("cid").And.NotContain("TOP-SECRET");
    }

    [Fact]
    public async Task RefreshedToken_IsPersisted_EvenWhenTheCallerCancelsDuringTheSave()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"access_token":"new","refresh_token":"rotated","expires_in":7200,"token_type":"bearer"}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","name":"n","username":"u"}}""");
        using var cts = new CancellationTokenSource();
        var store = new CancelAwareStore(new XOAuth2Token { AccessToken = "old", RefreshToken = "spent", ExpiresAt = DateTimeOffset.UnixEpoch }, cts);
        var client = TestClients.Create(handler, new XConnectOptions { OAuth2 = new XOAuth2Options { ClientId = "c" }, TokenStore = store });

        // The caller cancels while the refreshed token is being saved; the request itself is then (correctly) cancelled.
        var act = () => client.Users.GetMeAsync(cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        store.SavedToken.Should().NotBeNull("the rotated refresh token must not be lost");
        store.SavedToken!.RefreshToken.Should().Be("rotated");
    }

    private sealed class CancelAwareStore(XOAuth2Token initial, CancellationTokenSource cts) : IXTokenStore
    {
        public XOAuth2Token? SavedToken { get; private set; }

        public ValueTask<XOAuth2Token?> GetTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<XOAuth2Token?>(SavedToken ?? initial);

        public ValueTask SaveTokenAsync(XOAuth2Token token, CancellationToken cancellationToken)
        {
            cts.Cancel();   // the caller gives up just as the save starts
            cancellationToken.IsCancellationRequested.Should().BeFalse("the save must not observe the caller's cancellation");
            SavedToken = token;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task RateLimited_WithAResetInThePast_StillWaitsAtLeastOneSecond()
    {
        var time = new InstantTimeProvider();
        var reset = time.GetUtcNow().ToUnixTimeSeconds() - 30;   // clock skew: X's reset looks like it already happened
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.TooManyRequests, """{"title":"Too Many Requests"}""", r =>
            {
                r.Headers.Add("x-rate-limit-limit", "10");
                r.Headers.Add("x-rate-limit-remaining", "0");
                r.Headers.Add("x-rate-limit-reset", reset.ToString());
            })
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","text":"t"}}""");
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.Retry = new XRetryOptions { MaxRetries = 1, MaxDelay = TimeSpan.FromMinutes(1) }), time);

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        time.Delays.Should().ContainSingle().Which.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task PathParameters_ThatAreDotSegments_AreRejected(string id)
    {
        var handler = new QueueHandler();

        var act = () => TestClients.Create(handler).Posts.GetAsync(id, cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplayWindows_ThatCollapseToTheSameMinute_AreRejected()
    {
        var client = TestClients.Create(new QueueHandler(), new XConnectOptions().UseBearerToken("app"));
        var start = new DateTimeOffset(2026, 3, 4, 5, 5, 10, TimeSpan.Zero);

        var act = () => client.Webhooks.CreateReplayJobAsync("1", start, start.AddSeconds(20), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RateLimitHeaders_ThatOverflowInt_AreIgnored_NotWrapped()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","text":"t"}}""", r =>
        {
            r.Headers.Add("x-rate-limit-limit", "99999999999");
            r.Headers.Add("x-rate-limit-remaining", "5");
            r.Headers.Add("x-rate-limit-reset", "1900000000");
        });

        var response = await TestClients.Create(handler).Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        response.RateLimit.Should().BeNull();
    }
}
