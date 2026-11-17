using System.Net;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using XConnect.Authentication;
using XConnect.Models;

namespace XConnect.Tests;

public sealed class WebhooksClientTests
{
    private static XConnectClient AppOnly(QueueHandler handler) => TestClients.Create(handler, new XConnectOptions().UseBearerToken("app"));

    [Fact]
    public async Task Create_PostsTheUrl_AndReadsTheConfig()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"123","url":"https://example.com/hook","valid":true,"created_at":"2026-01-01T00:00:00.000Z"}}""");

        var response = await AppOnly(handler).Webhooks.CreateAsync("https://example.com/hook", CancellationToken.None);

        response.Data.Should().BeEquivalentTo(new XWebhookConfig { Id = "123", Url = "https://example.com/hook", Valid = true, CreatedAt = "2026-01-01T00:00:00.000Z" });
        handler.Requests.Single().Method.Should().Be(HttpMethod.Post);
        handler.Requests.Single().PathAndQuery.Should().Be("/2/webhooks");
        handler.Requests.Single().Body.Should().Be("""{"url":"https://example.com/hook"}""");
        handler.Requests.Single().Authorization.Should().Be("Bearer app");
    }

    [Theory]
    [InlineData("http://example.com/hook")]
    [InlineData("https://example.com:5000/hook")]
    [InlineData("/relative")]
    [InlineData(" ")]
    public async Task Create_RejectsUrlsXWillNotAccept_BeforeSending(string url)
    {
        var handler = new QueueHandler();

        var act = () => AppOnly(handler).Webhooks.CreateAsync(url, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetValidateAndDelete_UseTheDocumentedRoutes()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"1","url":"https://a.example/h","valid":false}],"meta":{"result_count":1}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"valid":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"deleted":true}}""");
        var client = AppOnly(handler);

        var list = await client.Webhooks.GetAsync(CancellationToken.None);
        var validation = await client.Webhooks.ValidateAsync("1", CancellationToken.None);
        var deleted = await client.Webhooks.DeleteAsync("1", CancellationToken.None);

        list.Data!.Single().Valid.Should().BeFalse();
        validation.Data!.Valid.Should().BeTrue();
        deleted.Data!.Deleted.Should().BeTrue();
        handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").Should().Equal("GET /2/webhooks", "PUT /2/webhooks/1", "DELETE /2/webhooks/1");
    }

    [Fact]
    public async Task ReplayJob_SendsUtcWindowsInTheDocumentedFormat()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"job_id":"j1","created_at":"2026-01-01T00:00:00.000Z"}}""");
        var start = new DateTimeOffset(2026, 3, 4, 7, 5, 0, TimeSpan.FromHours(2));   // 05:05 UTC
        var end = start.AddHours(1);

        var response = await AppOnly(handler).Webhooks.CreateReplayJobAsync("42", start, end, CancellationToken.None);

        response.Data!.JobId.Should().Be("j1");
        handler.Requests.Single().Body.Should().Be("""{"webhook_id":"42","from_date":"202603040505","to_date":"202603040605"}""");
        handler.Requests.Single().PathAndQuery.Should().Be("/2/webhooks/replay");
    }

    [Fact]
    public async Task ReplayJob_RejectsAnEmptyOrReversedWindow()
    {
        var now = DateTimeOffset.UtcNow;

        var act = () => AppOnly(new QueueHandler()).Webhooks.CreateReplayJobAsync("42", now, now, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task StreamLinks_UseTheSearchWebhookRoutes()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":[{"webhook_id":"9","application_id":"a","business_user_id":"b","instance_id":"i","created_at":"t","fields":["x"]}]}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"provisioned":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"deleted":true}}""");
        var client = AppOnly(handler);

        var links = await client.Webhooks.GetStreamLinksAsync(CancellationToken.None);
        var created = await client.Webhooks.CreateStreamLinkAsync("9", CancellationToken.None);
        var deleted = await client.Webhooks.DeleteStreamLinkAsync("9", CancellationToken.None);

        links.Data!.Single().Should().BeEquivalentTo(new XWebhookStreamLink { WebhookId = "9", ApplicationId = "a", BusinessUserId = "b", InstanceId = "i", CreatedAt = "t", Fields = ["x"] });
        created.Data!.Provisioned.Should().BeTrue();
        deleted.Data!.Deleted.Should().BeTrue();
        handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").Should().Equal(
            "GET /2/tweets/search/webhooks", "POST /2/tweets/search/webhooks/9", "DELETE /2/tweets/search/webhooks/9");
    }

    [Fact]
    public async Task AccountActivity_SubscriptionsUseUserContextOrAppOnlyAsDocumented()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"subscribed":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"subscribed":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"application_id":"a","webhook_id":"7","webhook_url":"https://e/h","subscriptions":[{"user_id":"u1"},{"user_id":"u2"}]}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"subscribed":false}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"account_name":"acme","provisioned_count":"10","subscriptions_count_all":"3","subscriptions_count_direct_messages":"1"}}""");
        var client = TestClients.Create(handler, new XConnectOptions().UseBearerToken("app").UseOAuth1(TestClients.Credentials));

        var created = await client.Webhooks.CreateSubscriptionAsync("7", CancellationToken.None);
        var validated = await client.Webhooks.ValidateSubscriptionAsync("7", CancellationToken.None);
        var list = await client.Webhooks.GetSubscriptionsAsync("7", CancellationToken.None);
        var removed = await client.Webhooks.DeleteSubscriptionAsync("7", "u1", CancellationToken.None);
        var count = await client.Webhooks.GetSubscriptionCountAsync(CancellationToken.None);

        created.Data!.Subscribed.Should().BeTrue();
        validated.Data!.Subscribed.Should().BeTrue();
        list.Data!.Subscriptions!.Select(s => s.UserId).Should().Equal("u1", "u2");
        removed.Data!.Subscribed.Should().BeFalse();
        count.Data!.SubscriptionsCountAll.Should().Be("3");
        handler.Requests[0].Body.Should().Be("{}");
        handler.Requests[0].Authorization.Should().StartWith("OAuth ", "user-context endpoints cannot use the app-only token");
        handler.Requests[1].Authorization.Should().StartWith("OAuth ");
        handler.Requests[2].Authorization.Should().Be("Bearer app");
        handler.Requests[3].Authorization.Should().Be("Bearer app");
        handler.Requests[4].Authorization.Should().Be("Bearer app");
        handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").Should().Equal(
            "POST /2/account_activity/webhooks/7/subscriptions/all",
            "GET /2/account_activity/webhooks/7/subscriptions/all",
            "GET /2/account_activity/webhooks/7/subscriptions/all/list",
            "DELETE /2/account_activity/webhooks/7/subscriptions/u1/all",
            "GET /2/account_activity/subscriptions/count");
    }

    [Fact]
    public async Task UserContextSubscription_WithOnlyABearerToken_FailsBeforeSending()
    {
        var handler = new QueueHandler();

        var act = () => AppOnly(handler).Webhooks.CreateSubscriptionAsync("7", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BlankIds_AreRejected()
    {
        var client = AppOnly(new QueueHandler());

        await ((Func<Task>)(() => client.Webhooks.ValidateAsync("", CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => client.Webhooks.DeleteSubscriptionAsync("1", " ", CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
    }
}

public sealed class ConnectionsClientTests
{
    private static XConnectClient AppOnly(QueueHandler handler) => TestClients.Create(handler, new XConnectOptions().UseBearerToken("app"));

    [Fact]
    public async Task GetHistory_SendsFilters_AndParsesConnections()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK,
            """{"data":[{"id":"c1","endpoint_name":"filtered_stream","client_ip":"1.2.3.4","connected_at":"a","disconnected_at":"b","disconnect_reason":"operator"}],"meta":{"result_count":1,"next_token":"n"}}""");

        var response = await AppOnly(handler).Connections.GetHistoryAsync(
            status: "all", endpoints: [XStreamEndpoints.FilteredStream, XStreamEndpoints.SampleStream], connectionFields: [XConnectionFields.Id, XConnectionFields.DisconnectReason], maxResults: 5, cancellationToken: CancellationToken.None);

        response.Data!.Single().DisconnectReason.Should().Be("operator");
        response.NextToken.Should().Be("n");
        var query = handler.Requests.Single().PathAndQuery;
        query.Should().StartWith("/2/connections?");
        query.Should().Contain("status=all").And.Contain("endpoints=filtered_stream%2Csample_stream").And.Contain("connection.fields=id%2Cdisconnect_reason").And.Contain("max_results=5");
    }

    [Fact]
    public async Task Terminate_SendsTheUuidsInADeleteBody()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"successful_kills":1,"failed_kills":1,"results":[{"uuid":"u1","success":true},{"uuid":"u2","success":false,"error_message":"gone"}]}}""");

        var response = await AppOnly(handler).Connections.TerminateAsync(["u1", "u2"], CancellationToken.None);

        response.Data!.SuccessfulKills.Should().Be(1);
        response.Data.Results![1].ErrorMessage.Should().Be("gone");
        handler.Requests.Single().Method.Should().Be(HttpMethod.Delete);
        handler.Requests.Single().Body.Should().Be("""{"uuids":["u1","u2"]}""");
    }

    [Fact]
    public async Task TerminateByEndpointAndAll_UseTheirRoutes()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"successful_kills":2,"failed_kills":0}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"successful_kills":5,"failed_kills":0}}""");
        var client = AppOnly(handler);

        var one = await client.Connections.TerminateByEndpointAsync(XStreamEndpoints.FilteredStream, CancellationToken.None);
        var all = await client.Connections.TerminateAllAsync(CancellationToken.None);

        one.Data!.SuccessfulKills.Should().Be(2);
        all.Data!.SuccessfulKills.Should().Be(5);
        handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").Should().Equal("DELETE /2/connections/filtered_stream", "DELETE /2/connections/all");
    }

    [Fact]
    public async Task InvalidArguments_AreRejectedBeforeSending()
    {
        var handler = new QueueHandler();
        var client = AppOnly(handler);

        await ((Func<Task>)(() => client.Connections.TerminateByEndpointAsync("../all", CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => client.Connections.TerminateAsync([], CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => client.Connections.TerminateAsync(["ok", " "], CancellationToken.None))).Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Connections_RefuseUserContextOnly()
    {
        var client = TestClients.Create(new QueueHandler(), TestClients.OAuth1Options());

        var act = () => client.Connections.TerminateAllAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>("connection management is app-only");
    }
}

public sealed class MyFollowersAndBuyersTests
{
    [Fact]
    public async Task MyFollowersAndFollowing_NeedNoUserLookup_AndAcceptOAuth1()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"1","name":"A","username":"a"}],"meta":{"result_count":1,"next_token":"nx"}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"2","name":"B","username":"b"}],"meta":{"result_count":1}}""");
        var client = TestClients.Create(handler);

        var followers = await client.Users.GetMyFollowersAsync(new XPageQuery { MaxResults = 50 }, CancellationToken.None);
        var following = await client.Users.GetMyFollowingAsync(cancellationToken: CancellationToken.None);

        followers.Data!.Single().Username.Should().Be("a");
        followers.NextToken.Should().Be("nx");
        following.Data!.Single().Id.Should().Be("2");
        handler.Requests.Should().HaveCount(2, "no /2/users/me lookup is needed");
        handler.Requests[0].PathAndQuery.Should().Be("/2/users/me/followers?max_results=50");
        handler.Requests[1].PathAndQuery.Should().Be("/2/users/me/following");
        handler.Requests[0].Authorization.Should().StartWith("OAuth ");
    }

    [Fact]
    public async Task MyFollowers_RefuseAppOnlyTokens()
    {
        var client = TestClients.Create(new QueueHandler(), new XConnectOptions().UseBearerToken("app"));

        var act = () => client.Users.GetMyFollowersAsync(cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task TicketBuyers_RequireOAuth2UserContext()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"id":"7","name":"Buyer","username":"buyer"}],"meta":{"result_count":1}}""");
        var options = new XConnectOptions { OAuth2 = new XOAuth2Options { ClientId = "c" }, TokenStore = new InMemoryXTokenStore(new XOAuth2Token { AccessToken = "AT" }) };
        var client = TestClients.Create(handler, options);

        var response = await client.Spaces.GetTicketBuyersAsync("sp1", cancellationToken: CancellationToken.None);

        response.Data!.Single().Username.Should().Be("buyer");
        handler.Requests.Single().PathAndQuery.Should().Be("/2/spaces/sp1/buyers");
        handler.Requests.Single().Authorization.Should().Be("Bearer AT");

        var bearerOnly = TestClients.Create(new QueueHandler(), new XConnectOptions().UseBearerToken("app"));
        await ((Func<Task>)(() => bearerOnly.Spaces.GetTicketBuyersAsync("sp1", cancellationToken: CancellationToken.None))).Should().ThrowAsync<InvalidOperationException>();
    }
}

public sealed class WebhookSecurityTests
{
    private const string Secret = "consumer-secret";

    [Fact]
    public void CrcToken_IsPrefixedBase64HmacSha256_OfTheChallenge()
    {
        var expected = "sha256=" + Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes("challenge")));

        XWebhookSecurity.CreateCrcResponseToken("challenge", Secret).Should().Be(expected);
    }

    [Fact]
    public void KnownVector_MatchesAnIndependentHmacImplementation()
    {
        // RFC 4231 test case 2: key "Jefe", data "what do ya want for nothing?".
        const string hex = "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843";

        var token = XWebhookSecurity.CreateCrcResponseToken("what do ya want for nothing?", "Jefe");

        Convert.ToHexStringLower(Convert.FromBase64String(token["sha256=".Length..])).Should().Be(hex);
    }

    [Fact]
    public void Signature_VerifiesTheRawBody_AndRejectsTampering()
    {
        var body = """{"event":"x"}"""u8.ToArray();
        var header = XWebhookSecurity.ComputeSignature(body, Secret);

        XWebhookSecurity.VerifySignature(body, header, Secret).Should().BeTrue();
        XWebhookSecurity.VerifySignature("""{"event":"y"}"""u8, header, Secret).Should().BeFalse();
        XWebhookSecurity.VerifySignature(body, header, "other-secret").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=")]
    [InlineData("sha1=abc")]
    [InlineData("not-a-signature")]
    public void MissingOrMalformedHeaders_AreRejected_WithoutThrowing(string? header)
    {
        XWebhookSecurity.VerifySignature("{}"u8, header, Secret).Should().BeFalse();
    }

    [Fact]
    public void EmptySecret_IsRejected()
    {
        var act = () => XWebhookSecurity.CreateCrcResponseToken("c", "");

        act.Should().Throw<ArgumentException>();
    }
}
