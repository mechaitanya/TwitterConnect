using System.Net;
using System.Text;
using AwesomeAssertions;

namespace XConnect.Tests;

public sealed class XClientTests
{
    private static readonly XCredentials Credentials = new("key", "secret", "token", "token-secret");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (XClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new XClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task CreatePostAsync_Success_ReturnsPostDetails()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.Created,
            """{"data":{"id":"1445880548472328192","text":"Hello world!"}}"""));

        var result = await client.CreatePostAsync(Credentials, "Hello world!");

        result.IsSuccess.Should().BeTrue();
        result.PostId.Should().Be("1445880548472328192");
        result.Text.Should().Be("Hello world!");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri.Should().Be(XClient.CreatePostUri);
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("OAuth");
        handler.LastBody.Should().Be("""{"text":"Hello world!"}""");
    }

    [Fact]
    public async Task CreatePostAsync_Failure_SurfacesProblemDetail()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.Forbidden,
            """{"title":"Forbidden","detail":"You are not allowed to create a Tweet with duplicate content.","status":403}"""));

        var result = await client.CreatePostAsync(Credentials, "again");

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        result.Error.Should().Contain("duplicate content");
    }

    [Fact]
    public async Task CreatePostAsync_ParsesRateLimitHeaders()
    {
        var (client, _) = Create(_ =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, """{"title":"Too Many Requests"}""");
            response.Headers.Add("x-rate-limit-limit", "100");
            response.Headers.Add("x-rate-limit-remaining", "0");
            response.Headers.Add("x-rate-limit-reset", "1700000000");
            return response;
        });

        var result = await client.CreatePostAsync(Credentials, "hi");

        result.RateLimit.Should().Be(new XRateLimit(100, 0, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)));
    }

    [Fact]
    public async Task CreatePostAsync_NonJsonErrorBody_FallsBackToRawBody()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream failure"),
        });

        var result = await client.CreatePostAsync(Credentials, "hi");

        result.Error.Should().Be("upstream failure");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatePostAsync_EmptyText_Throws(string text)
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.Created, "{}"));

        var act = () => client.CreatePostAsync(Credentials, text);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreatePostAsync_MissingCredential_Throws()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.Created, "{}"));

        var act = () => client.CreatePostAsync(Credentials with { AccessToken = "" }, "hi");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Credentials_ToString_DoesNotLeakSecrets()
    {
        var text = Credentials.ToString();

        text.Should().NotContain("secret").And.NotContain("token");
    }
}
