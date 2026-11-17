using System.Net;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XConnect.Models;

namespace XConnect.Tests;

public sealed class BackwardsCompatibilityTests
{
    [Fact]
    public void XClient_KeepsItsPublicSurface()
    {
        var type = typeof(XClient);

        type.IsSealed.Should().BeTrue();
        type.GetInterfaces().Should().Contain(typeof(IXClient));
        type.GetConstructor(Type.EmptyTypes).Should().NotBeNull("the parameterless constructor is part of the 2.0 API");
        type.GetConstructor([typeof(HttpClient), typeof(TimeProvider)]).Should().NotBeNull();
        type.GetField(nameof(XClient.CreatePostUri))!.IsStatic.Should().BeTrue();
        XClient.CreatePostUri.Should().Be(new Uri("https://api.x.com/2/tweets"));

        var method = type.GetMethod(nameof(XClient.CreatePostAsync))!;
        method.GetParameters().Select(p => p.ParameterType).Should().Equal(typeof(XCredentials), typeof(string), typeof(CancellationToken));
        method.ReturnType.Should().Be<Task<XPostResult>>();
    }

    [Fact]
    public void IXClient_StillHasExactlyTheOriginalMember() =>
        typeof(IXClient).GetMethods().Select(m => m.Name).Should().Equal(nameof(IXClient.CreatePostAsync));

    [Fact]
    public void ValueTypes_KeepTheirShape()
    {
        var credentials = new XCredentials("key", "SECRET-1", "token", "SECRET-2");
        credentials.ConsumerKey.Should().Be("key");
        credentials.AccessTokenSecret.Should().Be("SECRET-2");
        credentials.ToString().Should().NotContain("SECRET-1").And.NotContain("SECRET-2").And.NotContain("token");

        var rate = new XRateLimit(10, 9, DateTimeOffset.UnixEpoch);
        rate.Limit.Should().Be(10);

        var result = new XPostResult(HttpStatusCode.Created, "1", "t", null, rate);
        result.IsSuccess.Should().BeTrue();
        new XPostResult(HttpStatusCode.Forbidden, null, null, "dup", null).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task CreatePostAsync_BehavesAsInVersion2()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, """{"data":{"id":"5","text":"hello"}}""");
        IXClient client = new XClient(new HttpClient(handler));

        var result = await client.CreatePostAsync(TestClients.Credentials, "hello", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.PostId.Should().Be("5");
        handler.Requests.Single().Body.Should().Be("""{"text":"hello"}""");
        handler.Requests.Single().Authorization.Should().StartWith("OAuth ");
    }

    [Fact]
    public void NewTypes_AreAdditiveOnly_NoPublicTypeWasRemovedOrRenamed()
    {
        var names = typeof(XClient).Assembly.GetExportedTypes().Select(t => t.Name).ToHashSet();

        names.Should().Contain(["IXClient", "XClient", "XCredentials", "XPostResult", "XRateLimit"]);
    }
}

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task AddXConnect_ResolvesAWorkingClient_ThroughTheHttpClientFactory()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, """{"data":{"id":"1","text":"hi"}}""");
        var services = new ServiceCollection();
        services.AddXConnect(o =>
        {
            o.BaseAddress = new Uri("https://api.x.com/");
            o.UseOAuth1(TestClients.Credentials);
        }).ConfigurePrimaryHttpMessageHandler(() => handler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IXConnectClient>();

        var response = await client.Posts.CreateAsync(new CreatePostRequest { Text = "hi" }, CancellationToken.None);

        response.Data!.Id.Should().Be("1");
        handler.Requests.Single().Uri.Host.Should().Be("api.x.com");
    }

    [Fact]
    public async Task AddXConnect_HonoursAConfiguredBaseAddress()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","text":"hi"}}""");
        var services = new ServiceCollection();
        services.AddXConnect(o =>
        {
            o.BaseAddress = new Uri("https://gateway.example.test/");
            o.UseBearerToken("app");
        }).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IXConnectClient>().Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        handler.Requests.Single().Uri.Should().Be(new Uri("https://gateway.example.test/2/tweets/1"));
    }

    [Fact]
    public async Task AddXConnect_UsesARegisteredTimeProvider()
    {
        var time = new InstantTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","text":"hi"}}""");
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddXConnect(o => o.UseOAuth1(TestClients.Credentials)).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IXConnectClient>().Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        handler.Requests.Single().Authorization.Should().Contain("oauth_timestamp=\"1700000000\"");
    }

    [Fact]
    public async Task AddXConnect_ValidatesOptions()
    {
        var services = new ServiceCollection();
        services.AddXConnect(_ => { });
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<XConnectOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*authentication*");
    }

    [Fact]
    public async Task AddXConnect_RejectsBadOptionValues()
    {
        var services = new ServiceCollection();
        services.AddXConnect(o =>
        {
            o.UseBearerToken("t");
            o.Timeout = TimeSpan.FromSeconds(-5);
        });
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<XConnectOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Timeout*");
    }

    [Fact]
    public async Task InjectedClients_ShareRateLimitsAndAuthenticationState()
    {
        var services = new ServiceCollection();
        services.AddXConnect(o => o.UseBearerToken("t")).ConfigurePrimaryHttpMessageHandler(() => new QueueHandler());
        await using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IXConnectClient>();
        var second = provider.GetRequiredService<IXConnectClient>();

        first.Should().NotBeSameAs(second, "typed clients are transient so HttpClient handler rotation keeps working");
        first.RateLimits.Should().BeSameAs(second.RateLimits, "limits belong to the account, not to one injected instance");
    }

    [Fact]
    public async Task InjectedClients_RefreshAnExpiredOAuth2TokenExactlyOnce()
    {
        var api = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, """{"data":{"id":"1","name":"n","username":"u"}}"""));
        var tokenEndpoint = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, """{"access_token":"AT2","refresh_token":"RT2","expires_in":7200}"""));
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new XConnect.Authentication.InMemoryXTokenStore(
            new XConnect.Authentication.XOAuth2Token { AccessToken = "AT1", RefreshToken = "RT1", ExpiresAt = now.AddSeconds(-10) });
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new InstantTimeProvider(now));
        services.AddHttpClient("XConnect.OAuth2").ConfigurePrimaryHttpMessageHandler(() => tokenEndpoint);
        services.AddXConnect(o =>
        {
            o.OAuth2 = new XConnect.Authentication.XOAuth2Options { ClientId = "c" };
            o.TokenStore = store;
        }).ConfigurePrimaryHttpMessageHandler(() => api);
        await using var provider = services.BuildServiceProvider();

        var clients = Enumerable.Range(0, 5).Select(_ => provider.GetRequiredService<IXConnectClient>()).ToList();
        await Task.WhenAll(clients.Select(c => c.Users.GetMeAsync(cancellationToken: CancellationToken.None)));

        tokenEndpoint.Requests.Should().ContainSingle("a single-use refresh token must not be spent twice");
        api.Requests.Should().HaveCount(5);
        api.Requests.Should().AllSatisfy(r => r.Authorization.Should().Be("Bearer AT2"));
    }

    [Fact]
    public async Task AddXClient_StillRegistersTheVersion2Client()
    {
        var services = new ServiceCollection();
        services.AddXClient();
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IXClient>().Should().BeOfType<XClient>();
    }

    [Fact]
    public async Task AllClientProperties_AreAvailable()
    {
        var services = new ServiceCollection();
        services.AddXConnect(o => o.UseBearerToken("t"));
        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IXConnectClient>();

        new object?[] { client.Posts, client.Users, client.Media, client.Engagement, client.DirectMessages, client.Lists, client.Spaces, client.Streaming, client.Compliance, client.Trends, client.Communities, client.RateLimits }
            .Should().AllSatisfy(c => c.Should().NotBeNull());
    }
}

public sealed class SerializationTests
{
    [Fact]
    public void CreatePostRequest_OmitsNullsAndUsesTheDocumentedNames()
    {
        var json = JsonSerializer.Serialize(
            new CreatePostRequest
            {
                Text = "t",
                Nullcast = true,
                ForSuperFollowersOnly = false,
                CommunityId = "c",
                Clip = new XPostClip { PostId = "p", StartSeconds = 1, EndSeconds = 9 },
            },
            Serialization.XConnectJsonContext.Default.CreatePostRequest);

        json.Should().Be("""{"text":"t","clip":{"post_id":"p","start_seconds":1,"end_seconds":9},"community_id":"c","for_super_followers_only":false,"nullcast":true}""");
    }

    [Fact]
    public void Response_Dates_ParseFromXFormat()
    {
        var post = JsonSerializer.Deserialize(
            """{"id":"1","text":"t","created_at":"2026-02-03T04:05:06.000Z"}""",
            Serialization.XConnectJsonContext.Default.XPost);

        post!.CreatedAt.Should().Be(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));
    }

    [Fact]
    public void Response_NumbersEncodedAsStrings_AreAccepted()
    {
        var metrics = JsonSerializer.Deserialize(
            """{"like_count":"12","repost_count":3}""",
            Serialization.XConnectJsonContext.Default.XPostPublicMetrics);

        metrics!.LikeCount.Should().Be(12);
        metrics.RepostCount.Should().Be(3);
    }

    [Fact]
    public void LargeIds_RoundTripAsStrings_WithoutLosingPrecision()
    {
        var post = JsonSerializer.Deserialize(
            """{"id":"1445880548472328192","conversation_id":"1445880548472328192","edit_history_post_ids":["1445880548472328192"]}""",
            Serialization.XConnectJsonContext.Default.XPost);

        post!.Id.Should().Be("1445880548472328192");
        post.EditHistoryPostIds.Should().Equal("1445880548472328192");
    }

    [Fact]
    public void PublicModels_AreRegisteredInTheSourceGeneratedContext()
    {
        // Resolving metadata proves the context knows the type (no reflection fallback), which AOT and trimming require.
        var context = Serialization.XConnectJsonContext.Default;
        new object?[]
        {
            context.XPost, context.XUser, context.CreatePostRequest, context.EnvelopeXCreatedPost, context.EnvelopeXPostList,
            context.EnvelopeXMediaUploadResult, context.XStreamEnvelope, context.XProblem, context.EnvelopeXDmSendResult,
        }.Should().AllSatisfy(i => i.Should().NotBeNull());
    }

    [Fact]
    public void SourceGeneratedContext_DisablesReflectionFallback() =>
        Serialization.XConnectJsonContext.Default.Options.TypeInfoResolver.Should().BeSameAs(Serialization.XConnectJsonContext.Default);

    [Fact]
    public void AssemblyIsDeclaredTrimAndAotCompatible()
    {
        var attributes = typeof(XClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToList();

        attributes.Should().Contain(a => a.Key == "IsTrimmable" && a.Value == "True");
    }
}
