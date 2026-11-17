using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using XConnect.Authentication;

namespace XConnect.Tests;

public sealed class OAuth2Tests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static XOAuth2Options PublicOptions() => new()
    {
        ClientId = "client-id",
        RedirectUri = new Uri("https://app.example.test/callback"),
    };

    private static XOAuth2Client Create(QueueHandler handler, XOAuth2Options? options = null) =>
        new(new HttpClient(handler), options ?? PublicOptions(), new InstantTimeProvider(Now));

    [Fact]
    public void CodeChallenge_MatchesTheRfc7636Example()
    {
        // RFC 7636 appendix B.
        XOAuth2Client.CreateCodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")
            .Should().Be("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }

    [Fact]
    public void AuthorizationRequest_ContainsPkceStateAndScopes()
    {
        var client = Create(new QueueHandler());

        var request = client.CreateAuthorizationRequest([XScopes.TweetRead, XScopes.TweetWrite, XScopes.UsersRead, XScopes.OfflineAccess, XScopes.TweetRead]);

        request.Uri.GetLeftPart(UriPartial.Path).Should().Be("https://x.com/i/oauth2/authorize");
        var query = ParseQuery(request.Uri);
        query["response_type"].Should().Be("code");
        query["client_id"].Should().Be("client-id");
        query["redirect_uri"].Should().Be("https://app.example.test/callback");
        query["scope"].Should().Be("tweet.read tweet.write users.read offline.access");
        query["state"].Should().Be(request.State);
        query["code_challenge_method"].Should().Be("S256");
        query["code_challenge"].Should().Be(XOAuth2Client.CreateCodeChallenge(request.CodeVerifier));
        request.Uri.Query.Should().NotContain(request.CodeVerifier, "the verifier must never be sent to the authorization endpoint");
    }

    [Fact]
    public void AuthorizationRequests_UseFreshHighEntropyValues()
    {
        var client = Create(new QueueHandler());

        var requests = Enumerable.Range(0, 20).Select(_ => client.CreateAuthorizationRequest([XScopes.TweetRead])).ToList();

        requests.Select(r => r.State).Distinct().Should().HaveCount(20);
        requests.Select(r => r.CodeVerifier).Distinct().Should().HaveCount(20);
        requests.Should().AllSatisfy(r =>
        {
            r.CodeVerifier.Length.Should().BeInRange(43, 128, "RFC 7636 verifier length");
            r.CodeVerifier.Should().MatchRegex("^[A-Za-z0-9_-]+$");
            r.State.Length.Should().BeGreaterThanOrEqualTo(32);
        });
    }

    [Fact]
    public void AuthorizationRequest_RequiresScopesAndARedirectUri()
    {
        var client = Create(new QueueHandler());
        var noRedirect = Create(new QueueHandler(), new XOAuth2Options { ClientId = "c" });

        client.Invoking(c => c.CreateAuthorizationRequest([])).Should().Throw<ArgumentException>();
        noRedirect.Invoking(c => c.CreateAuthorizationRequest([XScopes.TweetRead])).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("abc", "", false)]
    [InlineData("abc", null, false)]
    [InlineData("", "", false)]
    public void ValidateState_ComparesSafely(string expected, string? actual, bool valid) =>
        XOAuth2Client.ValidateState(expected, actual).Should().Be(valid);

    [Fact]
    public async Task CompleteAuthorization_RejectsAMismatchedState_WithoutCallingTheTokenEndpoint()
    {
        var handler = new QueueHandler();
        var client = Create(handler);
        var request = client.CreateAuthorizationRequest([XScopes.TweetRead]);

        var act = () => client.CompleteAuthorizationAsync(request, new Uri("https://app.example.test/callback?code=abc&state=forged"));

        await act.Should().ThrowAsync<XAuthenticationException>().WithMessage("*state*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAuthorization_SurfacesUserDenial()
    {
        var client = Create(new QueueHandler());
        var request = client.CreateAuthorizationRequest([XScopes.TweetRead]);

        var act = () => client.CompleteAuthorizationAsync(request, new Uri($"https://app.example.test/callback?error=access_denied&state={request.State}"));

        await act.Should().ThrowAsync<XAuthenticationException>().WithMessage("*access_denied*");
    }

    [Fact]
    public async Task CompleteAuthorization_ExchangesTheCode_AsAPublicClient()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK,
            """{"token_type":"bearer","expires_in":7200,"access_token":"AT","scope":"tweet.read users.read offline.access","refresh_token":"RT"}""");
        var client = Create(handler);
        var request = client.CreateAuthorizationRequest([XScopes.TweetRead, XScopes.UsersRead, XScopes.OfflineAccess]);

        var token = await client.CompleteAuthorizationAsync(
            request,
            new Uri($"https://app.example.test/callback?code=the-code&state={Uri.EscapeDataString(request.State)}"));

        token.AccessToken.Should().Be("AT");
        token.RefreshToken.Should().Be("RT");
        token.ExpiresAt.Should().Be(Now.AddSeconds(7200));
        token.Scopes.Should().Equal("tweet.read", "users.read", "offline.access");

        var sent = handler.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Uri.Should().Be(new Uri("https://api.x.com/2/oauth2/token"));
        sent.Message.Headers.Authorization.Should().BeNull("public clients send client_id in the body");
        var form = ParseForm(sent.Body!);
        form["grant_type"].Should().Be("authorization_code");
        form["code"].Should().Be("the-code");
        form["code_verifier"].Should().Be(request.CodeVerifier);
        form["client_id"].Should().Be("client-id");
        form["redirect_uri"].Should().Be("https://app.example.test/callback");
    }

    [Fact]
    public async Task ExchangeCode_AsAConfidentialClient_UsesBasicAuthAndOmitsClientId()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"access_token":"AT","expires_in":60}""");
        var options = PublicOptions();
        options.ClientSecret = "client-secret";
        var client = Create(handler, options);

        await client.ExchangeCodeAsync("code", "verifier");

        var sent = handler.Requests.Single();
        sent.Message.Headers.Authorization!.Scheme.Should().Be("Basic");
        Encoding.UTF8.GetString(Convert.FromBase64String(sent.Message.Headers.Authorization.Parameter!)).Should().Be("client-id:client-secret");
        ParseForm(sent.Body!).Should().NotContainKey("client_id");
    }

    [Fact]
    public async Task ExchangeCode_Failure_ThrowsWithoutLeakingSecrets()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Value passed for the authorization code was invalid."}""");
        var options = PublicOptions();
        options.ClientSecret = "super-secret";
        var client = Create(handler, options);

        var act = () => client.ExchangeCodeAsync("code", "verifier");

        var exception = (await act.Should().ThrowAsync<XAuthenticationException>()).Which;
        exception.Message.Should().Contain("invalid").And.NotContain("super-secret").And.NotContain("verifier");
    }

    [Fact]
    public async Task ExchangeCode_ResponseWithoutAnAccessToken_Throws()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, "{}");
        var client = Create(handler);

        var act = () => client.ExchangeCodeAsync("code", "verifier");

        await act.Should().ThrowAsync<XAuthenticationException>().WithMessage("*access token*");
    }

    [Fact]
    public async Task Refresh_SendsTheRefreshToken_AndKeepsItWhenNotRotated()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"access_token":"AT2","expires_in":7200}""");
        var client = Create(handler);
        var old = new XOAuth2Token { AccessToken = "AT1", RefreshToken = "RT1", Scopes = ["tweet.read"], ExpiresAt = Now.AddSeconds(-1) };

        var fresh = await client.RefreshAsync(old);

        fresh.AccessToken.Should().Be("AT2");
        fresh.RefreshToken.Should().Be("RT1", "the old refresh token stays valid when X does not rotate it");
        fresh.Scopes.Should().Equal("tweet.read");
        var form = ParseForm(handler.Requests.Single().Body!);
        form["grant_type"].Should().Be("refresh_token");
        form["refresh_token"].Should().Be("RT1");
    }

    [Fact]
    public async Task Refresh_AdoptsARotatedRefreshToken()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"access_token":"AT2","refresh_token":"RT2","expires_in":7200}""");
        var client = Create(handler);

        var fresh = await client.RefreshAsync(new XOAuth2Token { AccessToken = "AT1", RefreshToken = "RT1" });

        fresh.RefreshToken.Should().Be("RT2");
    }

    [Fact]
    public async Task Refresh_WithoutARefreshToken_ExplainsTheOfflineAccessScope()
    {
        var client = Create(new QueueHandler());

        var act = () => client.RefreshAsync(new XOAuth2Token { AccessToken = "AT" });

        await act.Should().ThrowAsync<XAuthenticationException>().WithMessage("*offline.access*");
    }

    [Fact]
    public async Task Revoke_PostsTheToken()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, "{\"revoked\":true}");
        var client = Create(handler);

        await client.RevokeAsync("RT");

        var sent = handler.Requests.Single();
        sent.Uri.Should().Be(new Uri("https://api.x.com/2/oauth2/revoke"));
        ParseForm(sent.Body!)["token"].Should().Be("RT");
    }

    [Fact]
    public async Task Revoke_Failure_Throws()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.BadRequest, "{}");
        var client = Create(handler);

        var act = () => client.RevokeAsync("RT");

        await act.Should().ThrowAsync<XAuthenticationException>();
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(30, true)]
    [InlineData(61, false)]
    [InlineData(3600, false)]
    public void Token_ExpiryHonoursTheSkew(int secondsFromNow, bool expired)
    {
        var token = new XOAuth2Token { AccessToken = "AT", ExpiresAt = Now.AddSeconds(secondsFromNow) };

        token.IsExpired(Now, TimeSpan.FromMinutes(1)).Should().Be(expired);
    }

    [Fact]
    public void Token_WithoutExpiry_NeverExpires() =>
        new XOAuth2Token { AccessToken = "AT" }.IsExpired(Now, TimeSpan.FromMinutes(1)).Should().BeFalse();

    [Fact]
    public void SecretBearingTypes_MaskTheirSecretsInToString()
    {
        var token = new XOAuth2Token { AccessToken = "ACCESS-123", RefreshToken = "REFRESH-456" };
        var request = new XAuthorizationRequest { Uri = new Uri("https://x.com/i/oauth2/authorize"), State = "s", CodeVerifier = "VERIFIER-789" };

        token.ToString().Should().NotContain("ACCESS-123").And.NotContain("REFRESH-456");
        request.ToString().Should().NotContain("VERIFIER-789");
        new BearerTokenAuthenticator("BEARER-000").ToString().Should().NotContain("BEARER-000");
        TestClients.Credentials.ToString().Should().NotContain("secret");
    }

    [Fact]
    public async Task Authenticator_UsesAValidTokenWithoutRefreshing()
    {
        var handler = new QueueHandler();
        var store = new InMemoryXTokenStore(new XOAuth2Token { AccessToken = "AT", RefreshToken = "RT", ExpiresAt = Now.AddHours(1) });
        using var authenticator = new OAuth2UserAuthenticator(Create(handler), store, timeProvider: new InstantTimeProvider(Now));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.x.com/2/users/me");

        await authenticator.AuthenticateAsync(request, CancellationToken.None);

        request.Headers.Authorization.Should().Be(new AuthenticationHeaderValue("Bearer", "AT"));
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Authenticator_RefreshesAnExpiredToken_AndPersistsIt()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"access_token":"AT2","refresh_token":"RT2","expires_in":7200}""");
        var store = new InMemoryXTokenStore(new XOAuth2Token { AccessToken = "AT1", RefreshToken = "RT1", ExpiresAt = Now.AddSeconds(-5) });
        using var authenticator = new OAuth2UserAuthenticator(Create(handler), store, timeProvider: new InstantTimeProvider(Now));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.x.com/2/users/me");

        await authenticator.AuthenticateAsync(request, CancellationToken.None);

        request.Headers.Authorization!.Parameter.Should().Be("AT2");
        var saved = await store.GetTokenAsync(CancellationToken.None);
        saved!.AccessToken.Should().Be("AT2");
        saved.RefreshToken.Should().Be("RT2");
    }

    [Fact]
    public async Task Authenticator_RefreshesOnlyOnce_UnderConcurrentRequests()
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, """{"access_token":"AT2","expires_in":7200}"""));
        var store = new InMemoryXTokenStore(new XOAuth2Token { AccessToken = "AT1", RefreshToken = "RT1", ExpiresAt = Now.AddSeconds(-5) });
        using var authenticator = new OAuth2UserAuthenticator(Create(handler), store, timeProvider: new InstantTimeProvider(Now));

        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.x.com/2/users/me");
            await authenticator.AuthenticateAsync(request, CancellationToken.None);
            request.Headers.Authorization!.Parameter.Should().Be("AT2");
        }));

        handler.Requests.Should().ContainSingle("the refresh token may be single-use, so concurrent refreshes must be serialised");
    }

    [Fact]
    public async Task Authenticator_WithNoStoredToken_ExplainsWhatToDo()
    {
        using var authenticator = new OAuth2UserAuthenticator(Create(new QueueHandler()), new InMemoryXTokenStore());
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.x.com/2/users/me");

        var act = async () => await authenticator.AuthenticateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<XAuthenticationException>().WithMessage("*authorization flow*");
    }

    [Fact]
    public async Task Authenticator_WhenRefreshIsRejected_Throws_AndKeepsTheOldToken()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        var original = new XOAuth2Token { AccessToken = "AT1", RefreshToken = "RT1", ExpiresAt = Now.AddSeconds(-5) };
        var store = new InMemoryXTokenStore(original);
        using var authenticator = new OAuth2UserAuthenticator(Create(handler), store, timeProvider: new InstantTimeProvider(Now));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.x.com/2/users/me");

        var act = async () => await authenticator.AuthenticateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<XAuthenticationException>().WithMessage("*invalid_grant*");
        (await store.GetTokenAsync(CancellationToken.None)).Should().BeSameAs(original);
    }

    [Fact]
    public async Task Client_WithOAuth2Options_AuthenticatesUserContextRequestsWithTheStoredToken()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"7","name":"n","username":"u"}}""");
        var options = new XConnectOptions
        {
            OAuth2 = PublicOptions(),
            TokenStore = new InMemoryXTokenStore(new XOAuth2Token { AccessToken = "AT", ExpiresAt = Now.AddHours(1) }),
        };
        var client = TestClients.Create(handler, options, new InstantTimeProvider(Now));

        var me = await client.Users.GetMeAsync(cancellationToken: CancellationToken.None);

        me.Data!.Username.Should().Be("u");
        handler.Requests.Single().Authorization.Should().Be("Bearer AT");
    }

    [Fact]
    public async Task Unauthorized_FromTheApi_IsReportedAsAnAuthenticationError()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Unauthorized, """{"title":"Unauthorized","detail":"Unauthorized","type":"about:blank","status":401}""");
        var client = TestClients.Create(handler, new XConnectOptions().UseBearerToken("expired"));

        var response = await client.Users.GetByIdAsync("1", cancellationToken: CancellationToken.None);

        response.Error!.Kind.Should().Be(XApiErrorKind.Authentication);
    }

    private static Dictionary<string, string> ParseQuery(Uri uri) => ParseForm(uri.Query.TrimStart('?'));

    private static Dictionary<string, string> ParseForm(string text) =>
        text.Split('&').Select(p => p.Split('=', 2)).ToDictionary(
            p => Uri.UnescapeDataString(p[0].Replace('+', ' ')),
            p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
}
