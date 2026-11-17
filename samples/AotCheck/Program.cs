// Exercises the SDK under Native AOT with a stub transport, so no network or credentials are needed.
// Publish with: dotnet publish samples/AotCheck -c Release -r <rid>
using System.Net;
using System.Text;
using XConnect;
using XConnect.Models;

var handler = new StubHandler();
using var http = new HttpClient(handler);
var client = new XConnectClient(http, new XConnectOptions().UseOAuth1(new XCredentials("k", "s", "t", "ts")).UseBearerToken("app"));

// Request serialization + response deserialization through the source-generated context.
var created = await client.Posts.CreateAsync(new CreatePostRequest
{
    Text = "Hello from AOT",
    Poll = new XPostPoll { Options = ["a", "b"], DurationMinutes = 5 },
});
Check(created.IsSuccess && created.Data!.Id == "1445880548472328192", "create post");

var page = await client.Posts.SearchRecentAsync("dotnet", new XSearchQuery { MaxResults = 10 });
Check(page.Data!.Count == 1 && page.NextToken == "n1", "search + pagination meta");

var failure = await client.Posts.GetAsync("404");
Check(failure.Error?.Kind == XApiErrorKind.NotFound, "typed error");

var posts = new List<string?>();
await foreach (var post in XPaginator.ItemsAsync((t, ct) => client.Posts.SearchRecentAsync("dotnet", new XSearchQuery { PaginationToken = t }, ct)))
{
    posts.Add(post.Id);
}

Check(posts.Count == 2, "paginator");

// Newer models: nested request payloads, JsonElement-typed fields, call-to-action serialization and subtitle uploads.
var meta = await client.Media.SetMetadataAsync("7", new XMediaMetadata { AltText = "alt", AllowDownload = false, DomainWhitelist = ["example.com"] });
Check(meta.IsSuccess && meta.Data!.Id == "7" && handler.LastBody!.Contains("allow_download_status", StringComparison.Ordinal), "media metadata");

var analytics = await client.Media.GetAnalyticsAsync(["13_2"], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), "daily", ["video_views"]);
var item = analytics.Data!.Single();
Check(item.VideoViews!.Value.GetInt32() == 120 && item.TimestampedMetrics![0].Metrics!.PlaybackStart!.Value.GetString() == "n/a", "JsonElement fields");

var cta = await client.Posts.CreateAsync(new CreatePostRequest
{
    Text = "v",
    Media = new XPostMedia { MediaIds = ["m"], CallToActions = new XPostCallToActions { VisitSite = new XUrlCallToAction { Url = "https://example.com" } } },
});
Check(cta.IsSuccess && handler.LastBody!.Contains("\"visit_site\":{\"url\":\"https://example.com\"}", StringComparison.Ordinal), "call to action");

var news = await client.News.GetAsync("n1", ["summary"]);
Check(news.Data!.ClusterPostsResults![0].PostId == "7" && news.Data.Contexts is not null, "news");
var hook = await client.Webhooks.CreateAsync("https://example.com/hook");
Check(hook.Data!.Id == "5" && hook.Data.Valid == true && handler.LastBody == "{\"url\":\"https://example.com/hook\"}", "webhook create");

var replay = await client.Webhooks.CreateReplayJobAsync("5", new DateTimeOffset(2026, 3, 4, 5, 5, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 4, 6, 5, 0, TimeSpan.Zero));
Check(replay.Data!.JobId == "j" && handler.LastBody == "{\"webhook_id\":\"5\",\"from_date\":\"202603040505\",\"to_date\":\"202603040605\"}", "webhook replay");

var kill = await client.Connections.TerminateAsync(["u1"]);
Check(kill.Data!.SuccessfulKills == 1 && kill.Data.Results![0].Uuid == "u1" && handler.LastBody == "{\"uuids\":[\"u1\"]}", "terminate connections");

var token = XWebhookSecurity.CreateCrcResponseToken("what do ya want for nothing?", "Jefe");
Check(Convert.ToHexStringLower(Convert.FromBase64String(token["sha256=".Length..])) == "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843" && XWebhookSecurity.VerifySignature("{}"u8, XWebhookSecurity.ComputeSignature("{}"u8, "s"), "s"), "webhook signature");

var mine = await client.Users.GetMyFollowersAsync();
Check(mine.Data![0].Entities!.Url!.Urls![0].UnwoundUrl == "https://u" && mine.Data[0].ConnectionStatus![0] == "following", "new user fields");
Console.WriteLine("AOT check passed.");
return 0;

static void Check(bool condition, string what)
{
    if (!condition)
    {
        Console.Error.WriteLine("FAILED: " + what);
        Environment.Exit(1);
    }
}

internal sealed class StubHandler : HttpMessageHandler
{
    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        var (status, body) = (request.Method.Method, path) switch
        {
            ("POST", "/2/media/metadata") => (HttpStatusCode.OK, """{"data":{"id":"7"}}"""),
            ("GET", "/2/media/analytics") => (HttpStatusCode.OK, """{"data":[{"media_key":"13_2","video_views":120,"timestamped_metrics":[{"timestamp":"2026-01-01T00:00:00Z","metrics":{"playback_start":"n/a"}}]}]}"""),
            ("POST", "/2/webhooks") => (HttpStatusCode.OK, """{"data":{"id":"5","url":"https://example.com/hook","valid":true,"created_at":"t"}}"""),
            ("POST", "/2/webhooks/replay") => (HttpStatusCode.OK, """{"data":{"job_id":"j","created_at":"t"}}"""),
            ("DELETE", "/2/connections") => (HttpStatusCode.OK, """{"data":{"successful_kills":1,"failed_kills":0,"results":[{"uuid":"u1","success":true}]}}"""),
            ("GET", "/2/users/me/followers") => (HttpStatusCode.OK, """{"data":[{"id":"1","name":"n","username":"u","connection_status":["following"],"entities":{"url":{"urls":[{"unwound_url":"https://u","status":200}]}}}]}"""),
            ("GET", "/2/news/n1") => (HttpStatusCode.OK, """{"data":{"id":"n1","cluster_posts_results":[{"post_id":"7"}],"contexts":{"topics":["a"]}}}"""),
            ("POST", "/2/tweets") => (HttpStatusCode.Created, """{"data":{"id":"1445880548472328192","text":"Hello from AOT"}}"""),
            ("GET", "/2/tweets/search/recent") => (HttpStatusCode.OK, !request.RequestUri.Query.Contains("pagination_token", StringComparison.Ordinal)
                ? """{"data":[{"id":"1","text":"a"}],"meta":{"result_count":1,"next_token":"n1"}}"""
                : """{"data":[{"id":"2","text":"b"}],"meta":{"result_count":1}}"""),
            _ => (HttpStatusCode.NotFound, """{"title":"Not Found","detail":"missing","status":404}"""),
        };
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
