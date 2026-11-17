// These snippets mirror the README and the guides in docs/. They are compiled on every build so the documentation cannot drift
// from the API, but they are never executed (they would need real credentials). Run nothing here.
using Microsoft.Extensions.DependencyInjection;
using XConnect;
using XConnect.Authentication;
using XConnect.Clients;
using XConnect.Models;
using XConnect.Streaming;

Console.WriteLine("This project only demonstrates the API; it contains no runnable scenario. See README.md.");
return;

static class Snippets
{
    // README: "Quick start"
    public static async Task QuickStart(CancellationToken cancellationToken)
    {
        var credentials = new XCredentials("consumer_key", "consumer_secret", "access_token", "access_token_secret");
        using var http = new HttpClient();
        var client = new XConnectClient(http, new XConnectOptions().UseOAuth1(credentials));

        var response = await client.Posts.CreateAsync(new CreatePostRequest { Text = "Hello from XConnect!" }, cancellationToken);
        if (response.IsSuccess)
        {
            Console.WriteLine($"Posted: https://x.com/i/status/{response.Data!.Id}");
        }
        else
        {
            Console.WriteLine($"{response.Error!.Kind}: {response.Error.Message}");
        }
    }

    // README: "Dependency injection"
    public static void Registration(IServiceCollection services, XCredentials credentials) =>
        services.AddXConnect(options =>
        {
            options.BaseAddress = new Uri("https://api.x.com/");
            options.UseOAuth1(credentials);
        });

    // docs/publishing.md
    public static async Task Publishing(IXConnectClient client, Stream stream, CancellationToken cancellationToken)
    {
        var media = await client.Media.UploadImageAsync(stream, "announcement.png", cancellationToken);
        await client.Posts.CreateAsync(
            new CreatePostRequest { Text = "Our announcement", Media = new XPostMedia { MediaIds = [media.MediaId] } },
            cancellationToken);

        await client.Posts.CreateAsync(
            new CreatePostRequest { Text = "Vote!", Poll = new XPostPoll { Options = ["Yes", "No"], DurationMinutes = 1440 } },
            cancellationToken);

        await client.Posts.ReplyAsync("1445880548472328192", "Thanks!", cancellationToken);

        var thread = await client.Posts.CreateThreadAsync(["First post", "Second post", "Third post"], cancellationToken);
        if (!thread.IsComplete)
        {
            Console.WriteLine($"Stopped at post {thread.FailedIndex}: {thread.Error}");
        }
    }

    // docs/media.md
    public static async Task Media(IXConnectClient client, Stream video, CancellationToken cancellationToken)
    {
        var result = await client.Media.UploadAsync(
            video,
            new XMediaUploadOptions
            {
                Category = XMediaCategory.TweetVideo,
                FileName = "launch.mp4",
                Progress = new Progress<XUploadProgress>(p => Console.WriteLine($"{p.BytesSent}/{p.TotalBytes}")),
            },
            cancellationToken);
        await client.Media.SetAltTextAsync(result.MediaId, "Our product launch video", cancellationToken);
    }

    // docs/media.md: metadata, call-to-action, subtitles
    public static async Task MediaExtras(IXConnectClient client, XMediaUploadResult video, Stream srt, CancellationToken cancellationToken)
    {
        await client.Media.SetMetadataAsync(video.MediaId, new XMediaMetadata { AltText = "A red bicycle", AllowDownload = false, DomainWhitelist = ["example.com"] }, cancellationToken);

        await client.Posts.CreateAsync(
            new CreatePostRequest
            {
                Text = "Watch",
                Media = new XPostMedia
                {
                    MediaIds = [video.MediaId],
                    Title = "Launch",
                    CallToActions = new XPostCallToActions { VisitSite = new XUrlCallToAction { Url = "https://example.com" } },
                },
            },
            cancellationToken);

        var subtitles = await client.Media.UploadAsync(srt, new XMediaUploadOptions { Category = XMediaCategory.Subtitles, FileName = "en.srt" }, cancellationToken);
        await client.Media.CreateSubtitlesAsync(video.MediaId, XSubtitleCategory.TweetVideo, subtitles.MediaId, "en", "English", cancellationToken);
    }

    // docs/rate-limits.md: concurrency cap
    public static XConnectOptions Throttled(XCredentials credentials) =>
        new XConnectOptions { MaxConcurrentRequests = 4 }.UseOAuth1(credentials);

    // docs/streaming.md: partitioned (Enterprise) stream
    public static async Task Partitioned(IXConnectClient client, CancellationToken cancellationToken)
    {
        await foreach (var e in client.Streaming.StreamPartitionAsync(XPostStreamKind.Sample10, partition: 1, cancellationToken: cancellationToken))
        {
            Console.WriteLine(e.Post?.Id);
        }
    }

    // README: newer API areas
    public static async Task MoreAreas(IXConnectClient client, CancellationToken cancellationToken)
    {
        var folders = await client.Engagement.GetBookmarkFoldersAsync(cancellationToken: cancellationToken);
        var pinned = await client.Lists.GetPinnedAsync(cancellationToken: cancellationToken);
        var trends = await client.Trends.GetPersonalizedAsync(cancellationToken: cancellationToken);
        var credits = await client.Usage.GetCreditsAsync(cancellationToken);
        var analytics = await client.Posts.GetAnalyticsAsync(["1445880548472328192"], DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, "daily", ["impressions", "likes"], cancellationToken);
        Console.WriteLine($"{folders.Data?.Count} {pinned.Data?.Count} {trends.Data?.Count} {credits.Data?.TotalBalance} {analytics.Data?.Count}");
    }

    // docs/pagination.md
    public static async Task Pagination(IXConnectClient client, CancellationToken cancellationToken)
    {
        await foreach (var post in XPaginator.ItemsAsync(
            (token, ct) => client.Posts.SearchRecentAsync("from:xdevelopers", new XSearchQuery { PaginationToken = token, MaxResults = 100 }, ct),
            new XPaginationOptions { MaxPages = 5 },
            cancellationToken))
        {
            Console.WriteLine(post.Text);
        }

        var firstPage = await client.Users.GetFollowersAsync("2244994945", new XPageQuery { MaxResults = 1000 }, cancellationToken);
        var next = firstPage.NextToken;
        Console.WriteLine(next);
    }

    // docs/authentication.md: OAuth 2.0 Authorization Code with PKCE
    public static async Task OAuth2(HttpClient http, Uri callbackUri, CancellationToken cancellationToken)
    {
        var oauth = new XOAuth2Client(http, new XOAuth2Options
        {
            ClientId = "client_id",
            RedirectUri = new Uri("https://app.example.com/callback"),
        });

        var authorization = oauth.CreateAuthorizationRequest([XScopes.TweetRead, XScopes.TweetWrite, XScopes.UsersRead, XScopes.OfflineAccess]);
        Console.WriteLine($"Send the user to {authorization.Uri}; keep State and CodeVerifier server-side.");

        // In your callback handler:
        var token = await oauth.CompleteAuthorizationAsync(authorization, callbackUri, cancellationToken);

        var client = new XConnectClient(http, new XConnectOptions
        {
            OAuth2 = new XOAuth2Options { ClientId = "client_id", RedirectUri = new Uri("https://app.example.com/callback") },
            TokenStore = new InMemoryXTokenStore(token),
        });
        var me = await client.Users.GetMeAsync(cancellationToken: cancellationToken);
        Console.WriteLine(me.Data?.Username);
    }

    // docs/error-handling.md
    public static async Task ErrorHandling(IXConnectClient client, CancellationToken cancellationToken)
    {
        var response = await client.Posts.CreateAsync(new CreatePostRequest { Text = "hi" }, cancellationToken);
        switch (response.Error?.Kind)
        {
            case null:
                break;
            case XApiErrorKind.DuplicatePost:
                Console.WriteLine("Already posted.");
                break;
            case XApiErrorKind.RateLimited:
                Console.WriteLine($"Try again at {response.RateLimit?.ResetsAt:u}");
                break;
            default:
                Console.WriteLine(response.Error);
                break;
        }
    }

    // docs/streaming.md
    public static async Task Streaming(IXConnectClient client, CancellationToken cancellationToken)
    {
        await client.Streaming.AddRulesAsync([new XStreamRuleDraft("dotnet has:media", "dotnet-media")], cancellationToken: cancellationToken);
        await foreach (var e in client.Streaming.StreamFilteredAsync(cancellationToken: cancellationToken))
        {
            Console.WriteLine($"{(e.MatchingRules is { Count: > 0 } rules ? rules[0].Tag : null)}: {e.Post?.Text}");
        }
    }

    // docs/rate-limits.md
    public static void RateLimits(IXConnectClient client)
    {
        if (client.RateLimits.TryGet(HttpMethod.Post, "/2/tweets", out var limit) && limit is { Remaining: 0 })
        {
            Console.WriteLine($"Posting is limited until {limit.ResetsAt:u}");
        }
    }

    // README: version 2.0 API, unchanged
    public static async Task WebhooksAndConnections(IXConnectClient client, ReadOnlyMemory<byte> receivedBody, string signatureHeader, string crcToken, string clientSecret, CancellationToken cancellationToken)
    {
        var created = await client.Webhooks.CreateAsync("https://example.com/webhooks/x", cancellationToken);
        if (created.IsSuccess)
        {
            var id = created.Data!.Id!;
            await client.Webhooks.ValidateAsync(id, cancellationToken);
            await client.Webhooks.CreateReplayJobAsync(id, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1), cancellationToken);
            await client.Webhooks.CreateSubscriptionAsync(id, cancellationToken);
        }

        // The receiving side (inside your HTTP endpoint):
        var responseToken = XWebhookSecurity.CreateCrcResponseToken(crcToken, clientSecret);
        var authentic = XWebhookSecurity.VerifySignature(receivedBody.Span, signatureHeader, clientSecret);
        Console.WriteLine($"{responseToken.Length}, {authentic}");

        var connections = await client.Connections.GetHistoryAsync(status: "active", endpoints: [XStreamEndpoints.FilteredStream], cancellationToken: cancellationToken);
        await client.Connections.TerminateByEndpointAsync(XStreamEndpoints.FilteredStream, cancellationToken);
        var mine = await client.Users.GetMyFollowersAsync(new XPageQuery { MaxResults = 100 }, cancellationToken);
        Console.WriteLine($"{connections.Data?.Count}, {mine.Data?.Count}");
    }

    public static async Task LegacyApi(XCredentials credentials, CancellationToken cancellationToken)
    {
        IXClient client = new XClient();
        XPostResult result = await client.CreatePostAsync(credentials, "Hello from XConnect!", cancellationToken);
        Console.WriteLine(result.IsSuccess);
    }
}
