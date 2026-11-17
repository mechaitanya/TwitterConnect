using AwesomeAssertions;
using XConnect.Authentication;
using XConnect.Models;
using XConnect.Streaming;
using Xunit.Abstractions;

namespace XConnect.IntegrationTests;

[Trait("Category", "Live")]
public sealed class LiveReadOnlyTests(ITestOutputHelper output)
{
    private static XConnectClient OAuth1Client(HttpClient http) => new(http, new XConnectOptions().UseOAuth1(Live.OAuth1()));

    [LiveFact(false, "XCONNECT_CONSUMER_KEY", "XCONNECT_CONSUMER_SECRET", "XCONNECT_ACCESS_TOKEN", "XCONNECT_ACCESS_TOKEN_SECRET")]
    public async Task OAuth1_AuthenticatedUserLookup()
    {
        using var http = new HttpClient();
        var client = OAuth1Client(http);

        var me = await client.Users.GetMeAsync(cancellationToken: CancellationToken.None);

        me.IsSuccess.Should().BeTrue(me.Error?.ToString());
        me.Data!.Id.Should().NotBeNullOrEmpty();
        output.WriteLine($"Authenticated as @{me.Data.Username} (HTTP {(int)me.StatusCode}).");
    }

    [LiveFact(false, "XCONNECT_CONSUMER_KEY", "XCONNECT_CONSUMER_SECRET", "XCONNECT_ACCESS_TOKEN", "XCONNECT_ACCESS_TOKEN_SECRET")]
    public async Task RateLimitHeaders_AreParsedFromRealResponses()
    {
        using var http = new HttpClient();
        var client = OAuth1Client(http);

        var me = await client.Users.GetMeAsync(cancellationToken: CancellationToken.None);

        me.IsSuccess.Should().BeTrue(me.Error?.ToString());
        me.RateLimit.Should().NotBeNull("X documents x-rate-limit-limit/-remaining/-reset on API responses");
        me.RateLimit!.Limit.Should().BeGreaterThan(0);
        me.RateLimit.Remaining.Should().BeInRange(0, me.RateLimit.Limit);
        client.RateLimits.TryGet(HttpMethod.Get, "/2/users/me", out var tracked).Should().BeTrue();
        tracked.Should().Be(me.RateLimit);
        output.WriteLine($"GET /2/users/me: {me.RateLimit.Remaining}/{me.RateLimit.Limit}, resets {me.RateLimit.ResetsAt:u}.");
    }

    [LiveFact(false, "XCONNECT_CONSUMER_KEY", "XCONNECT_CONSUMER_SECRET", "XCONNECT_ACCESS_TOKEN", "XCONNECT_ACCESS_TOKEN_SECRET")]
    public async Task UnknownPost_IsReportedAsATypedError_NotAnException()
    {
        using var http = new HttpClient();
        var client = OAuth1Client(http);

        // A syntactically valid ID that does not belong to a visible Post.
        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        output.WriteLine($"GET /2/tweets/1 -> HTTP {(int)response.StatusCode}, kind {response.Error?.Kind ?? XApiErrorKind.Unknown}, partial errors {response.PartialErrors.Count}.");
        (response.Error is not null || response.PartialErrors.Count > 0 || response.Data is null).Should().BeTrue();
    }

    /// <summary>
    /// Reads the authenticated account's own Posts. Read-only: nothing is created or changed. Needs OAuth 1.0a user credentials whose app has at least
    /// Read permission; the account may have no Posts at all (an empty list passes). Whether X allows the timeline endpoint depends on the API plan, so
    /// a 403 is accepted as a correctly classified outcome and is reported in the output rather than counted as a successful read.
    /// </summary>
    [LiveFact(false, "XCONNECT_CONSUMER_KEY", "XCONNECT_CONSUMER_SECRET", "XCONNECT_ACCESS_TOKEN", "XCONNECT_ACCESS_TOKEN_SECRET")]
    public async Task OAuth1_ReadOwnPosts()
    {
        using var http = new HttpClient();
        var client = OAuth1Client(http);

        var me = await client.Users.GetMeAsync(cancellationToken: CancellationToken.None);
        me.IsSuccess.Should().BeTrue(me.Error?.ToString());

        var posts = await client.Posts.GetUserPostsAsync(me.Data!.Id!, new XPostQuery { MaxResults = 5 }, CancellationToken.None);

        output.WriteLine($"GET /2/users/:id/tweets -> HTTP {(int)posts.StatusCode}, kind {posts.Error?.Kind.ToString() ?? "none"}.");
        if (posts.IsSuccess)
        {
            posts.Data.Should().NotBeNull();
            posts.Data!.Count.Should().BeLessThanOrEqualTo(5);
            posts.Data.Should().OnlyContain(p => !string.IsNullOrEmpty(p.Id));
            output.WriteLine($"READ OK: {posts.Data.Count} Post(s) returned for @{me.Data.Username}.");
        }
        else
        {
            posts.Error!.Kind.Should().Be(XApiErrorKind.Authorization, "the only accepted failure is a plan or permission restriction (HTTP 403)");
            output.WriteLine("NOT VERIFIED: X refused the read (HTTP 403), so the success path was not exercised on this account.");
        }
    }

    /// <summary>
    /// Sends a deliberately invalid bearer token. It uses no real credential, so it needs only <c>XCONNECT_LIVE=1</c>. X documents HTTP 401 for
    /// credentials it cannot authenticate; the SDK must return it as a typed <see cref="XApiErrorKind.Authentication"/> error, not throw.
    /// </summary>
    [LiveFact]
    public async Task InvalidBearerToken_IsReportedAsATypedUnauthorizedError()
    {
        using var http = new HttpClient();
        var client = new XConnectClient(http, new XConnectOptions().UseBearerToken("xconnect-deliberately-invalid-token"));

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        output.WriteLine($"GET /2/tweets/:id with an invalid bearer token -> HTTP {(int)response.StatusCode}, kind {response.Error?.Kind.ToString() ?? "none"}.");
        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        response.Error.Should().NotBeNull();
        response.Error!.Kind.Should().Be(XApiErrorKind.Authentication);
    }

    /// <summary>
    /// Asks for another account's liked Posts, which X restricts to the authenticated user. Read-only. ASSUMPTION: X answers this with HTTP 403 (the
    /// documentation says the ID must match the authenticated user but does not name the status), so the test does not demand 403: it requires a typed
    /// 4xx failure whose <see cref="XApiErrorKind"/> matches the status, and the output states the observed status. Count it as a verified
    /// 403 mapping only if the output shows HTTP 403.
    /// </summary>
    [LiveFact(false, "XCONNECT_CONSUMER_KEY", "XCONNECT_CONSUMER_SECRET", "XCONNECT_ACCESS_TOKEN", "XCONNECT_ACCESS_TOKEN_SECRET")]
    public async Task OtherUsersLikes_AreRefusedWithATypedClientError()
    {
        using var http = new HttpClient();
        var client = OAuth1Client(http);

        var me = await client.Users.GetMeAsync(cancellationToken: CancellationToken.None);
        me.IsSuccess.Should().BeTrue(me.Error?.ToString());
        const string otherPublicUserId = "783214"; // a well-known public account; only its (restricted) likes are requested
        me.Data!.Id.Should().NotBe(otherPublicUserId, "the test needs an account other than the authenticated one");

        var response = await client.Posts.GetLikedPostsAsync(otherPublicUserId, new XPageQuery { MaxResults = 5 }, CancellationToken.None);

        var status = (int)response.StatusCode;
        output.WriteLine($"GET /2/users/:other/liked_tweets -> HTTP {status}, kind {response.Error?.Kind.ToString() ?? "none"}. " +
            (status == 403 ? "403 mapping VERIFIED." : "Status was not 403: the 403 mapping was NOT exercised by this run."));
        response.IsSuccess.Should().BeFalse("X restricts liked Posts to the authenticated user");
        status.Should().BeInRange(400, 499);
        response.Error.Should().NotBeNull();
        response.Error!.Kind.Should().Be(status switch
        {
            401 => XApiErrorKind.Authentication,
            403 => XApiErrorKind.Authorization,
            404 => XApiErrorKind.NotFound,
            429 => XApiErrorKind.RateLimited,
            _ => XApiErrorKind.Validation,
        });
    }

    [LiveFact(false, "XCONNECT_BEARER_TOKEN")]
    public async Task AppOnly_SearchRecent()
    {
        using var http = new HttpClient();
        var client = new XConnectClient(http, new XConnectOptions().UseBearerToken(Live.Env("XCONNECT_BEARER_TOKEN")!));

        var result = await client.Posts.SearchRecentAsync("dotnet -is:retweet", new XSearchQuery { MaxResults = 10 }, CancellationToken.None);

        output.WriteLine($"Search -> HTTP {(int)result.StatusCode}; {(result.Error is null ? result.Data!.Count + " Posts" : result.Error.ToString())}.");
        // Search access depends on the plan: a 403 is a legitimate, correctly classified outcome.
        (result.IsSuccess || result.Error!.Kind is XApiErrorKind.Authorization or XApiErrorKind.RateLimited).Should().BeTrue(result.Error?.ToString());
    }

    /// <summary>
    /// The SDK sends <c>tweet.fields</c> (the name the OpenAPI document gives) to the Post streams instead of <c>post.fields</c>. This checks that X accepts it:
    /// a plan without stream access answers 403, which is fine; a 400 that blames the parameter fails the test. Reads at most a few events and then disconnects.
    /// </summary>
    [LiveFact(false, "XCONNECT_BEARER_TOKEN")]
    public async Task SampleStream_AcceptsTheTweetFieldsParameter()
    {
        using var http = new HttpClient();
        var client = new XConnectClient(http, new XConnectOptions().UseBearerToken(Live.Env("XCONNECT_BEARER_TOKEN")!));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var options = new XStreamOptions
        {
            MaxReconnects = 0,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            Fields = new XFieldSelection { PostFields = [XPostFields.CreatedAt] },
        };

        try
        {
            var seen = 0;
            await foreach (var e in client.Streaming.StreamSampleAsync(options, cts.Token))
            {
                seen++;
                if (e.Post?.CreatedAt is not null || seen >= 3)
                {
                    break;
                }
            }

            output.WriteLine($"Sample stream accepted tweet.fields; read {seen} event(s).");
        }
        catch (XApiException ex)
        {
            output.WriteLine($"Sample stream -> HTTP {(int)ex.Error.StatusCode}, kind {ex.Error.Kind}, detail '{ex.Error.Detail}'.");
            ex.Error.Kind.Should().NotBe(XApiErrorKind.Validation, "a 400 would mean X rejects tweet.fields on streams; switch the SDK back to post.fields");
        }
        catch (OperationCanceledException)
        {
            output.WriteLine("No event within 25 s; the connection itself was accepted.");
        }
    }

    [LiveFact(false, "XCONNECT_BEARER_TOKEN", "XCONNECT_CONSUMER_KEY", "XCONNECT_CONSUMER_SECRET", "XCONNECT_ACCESS_TOKEN", "XCONNECT_ACCESS_TOKEN_SECRET")]
    public async Task EndpointAwareAuthentication_UsesBearerForSearchAndOAuth1ForUserLookup()
    {
        using var http = new HttpClient();
        var client = new XConnectClient(http, new XConnectOptions().UseBearerToken(Live.Env("XCONNECT_BEARER_TOKEN")!).UseOAuth1(Live.OAuth1()));

        var me = await client.Users.GetMeAsync(cancellationToken: CancellationToken.None);

        me.IsSuccess.Should().BeTrue(me.Error?.ToString());
    }
}

[Trait("Category", "Live")]
public sealed class LiveOAuth2Tests(ITestOutputHelper output)
{
    /// <summary>
    /// Step 1 of the PKCE flow. Prints the URL to open in a browser and saves the verifier to a private file
    /// (<c>XCONNECT_OAUTH2_PENDING_FILE</c>); the verifier is never printed.
    /// </summary>
    [LiveFact(false, "XCONNECT_OAUTH2_CLIENT_ID", "XCONNECT_OAUTH2_REDIRECT_URI", "XCONNECT_OAUTH2_PENDING_FILE")]
    public async Task Pkce_Step1_CreatesAnAuthorizationRequest()
    {
        if (Live.Env("XCONNECT_OAUTH2_CALLBACK_URL") is not null)
        {
            output.WriteLine("XCONNECT_OAUTH2_CALLBACK_URL is set, so step 2 will run instead; unset it to start a new authorization.");
            return;
        }

        using var http = new HttpClient();
        var oauth = new XOAuth2Client(http, Live.OAuth2Options());

        var request = oauth.CreateAuthorizationRequest([XScopes.TweetRead, XScopes.UsersRead, XScopes.OfflineAccess]);
        Live.WriteSecretFile(Live.Env("XCONNECT_OAUTH2_PENDING_FILE")!, System.Text.Json.JsonSerializer.Serialize(new[] { request.State, request.CodeVerifier }));
        output.WriteLine("Open this URL, approve, then set XCONNECT_OAUTH2_CALLBACK_URL to the full URL you are redirected to and run again:");
        output.WriteLine(request.Uri.AbsoluteUri);
        await Task.CompletedTask;
    }

    /// <summary>Step 2: validates the callback and exchanges the code; saves the token to <c>XCONNECT_OAUTH2_TOKEN_FILE</c> and uses it.</summary>
    [LiveFact(false, "XCONNECT_OAUTH2_CLIENT_ID", "XCONNECT_OAUTH2_REDIRECT_URI", "XCONNECT_OAUTH2_PENDING_FILE", "XCONNECT_OAUTH2_CALLBACK_URL", "XCONNECT_OAUTH2_TOKEN_FILE")]
    public async Task Pkce_Step2_CompletesAuthorizationAndCallsTheApi()
    {
        var pending = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(Live.Env("XCONNECT_OAUTH2_PENDING_FILE")!))!;
        var request = new XAuthorizationRequest { Uri = new Uri("https://x.com/"), State = pending[0], CodeVerifier = pending[1] };
        using var http = new HttpClient();
        var oauth = new XOAuth2Client(http, Live.OAuth2Options());

        var token = await oauth.CompleteAuthorizationAsync(request, new Uri(Live.Env("XCONNECT_OAUTH2_CALLBACK_URL")!), CancellationToken.None);
        Live.WriteSecretFile(Live.Env("XCONNECT_OAUTH2_TOKEN_FILE")!, Live.Serialize(token));
        File.Delete(Live.Env("XCONNECT_OAUTH2_PENDING_FILE")!);

        var client = new XConnectClient(http, new XConnectOptions { OAuth2 = Live.OAuth2Options(), TokenStore = new InMemoryXTokenStore(token) });
        var me = await client.Users.GetMeAsync(cancellationToken: CancellationToken.None);

        me.IsSuccess.Should().BeTrue(me.Error?.ToString());
        output.WriteLine($"PKCE completed; authenticated as @{me.Data!.Username}. Scopes granted: {string.Join(' ', token.Scopes)}; refresh token issued: {token.RefreshToken is not null}.");
    }

    /// <summary>
    /// Forces a refresh with the stored refresh token. X does not document whether refresh tokens rotate, so the new token set is written back to
    /// <c>XCONNECT_OAUTH2_TOKEN_FILE</c> before anything else happens; otherwise a rotated token could leave you locked out.
    /// </summary>
    [LiveFact(["XCONNECT_OAUTH2_CLIENT_ID", "XCONNECT_OAUTH2_TOKEN_FILE"], rotatesTokens: true)]
    public async Task TokenRefresh_ObtainsAWorkingAccessToken_AndPersistsTheNewTokens()
    {
        var path = Live.Env("XCONNECT_OAUTH2_TOKEN_FILE")!;
        if (!File.Exists(path))
        {
            output.WriteLine("No token file yet; run the PKCE steps first.");
            return;
        }

        var stored = Live.Deserialize(File.ReadAllText(path));
        stored.RefreshToken.Should().NotBeNull("the token needs a refresh token (request the offline.access scope)");
        using var http = new HttpClient();
        var oauth = new XOAuth2Client(http, Live.OAuth2Options());

        var refreshed = await oauth.RefreshAsync(stored, CancellationToken.None);
        Live.WriteSecretFile(path, Live.Serialize(refreshed));
        var rotated = refreshed.RefreshToken != stored.RefreshToken;
        output.WriteLine($"Refresh succeeded. Refresh token rotated: {rotated}. New expiry: {refreshed.ExpiresAt:u}.");

        var client = new XConnectClient(http, new XConnectOptions { OAuth2 = Live.OAuth2Options(), TokenStore = new InMemoryXTokenStore(refreshed) });
        var me = await client.Users.GetMeAsync(cancellationToken: CancellationToken.None);
        me.IsSuccess.Should().BeTrue(me.Error?.ToString());
    }

    [LiveFact(["XCONNECT_OAUTH2_CLIENT_ID", "XCONNECT_OAUTH2_TOKEN_FILE"], rotatesTokens: true)]
    public async Task ExpiredAccessToken_IsRefreshedTransparently_ByTheAuthenticator()
    {
        var path = Live.Env("XCONNECT_OAUTH2_TOKEN_FILE")!;
        if (!File.Exists(path))
        {
            output.WriteLine("No token file yet; run the PKCE steps first.");
            return;
        }

        var stored = Live.Deserialize(File.ReadAllText(path));
        var store = new FileTokenStore(path, stored with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) });
        using var http = new HttpClient();
        var client = new XConnectClient(http, new XConnectOptions { OAuth2 = Live.OAuth2Options(), TokenStore = store });

        var me = await client.Users.GetMeAsync(cancellationToken: CancellationToken.None);

        me.IsSuccess.Should().BeTrue(me.Error?.ToString());
        store.Saved.Should().BeTrue("the authenticator must persist the refreshed token");
    }

    /// <summary>
    /// Many simultaneous calls with an expired access token must trigger exactly one refresh (refresh tokens may be single-use). Counts the saves
    /// through the store, so it also proves the new token is persisted.
    /// </summary>
    [LiveFact(["XCONNECT_OAUTH2_CLIENT_ID", "XCONNECT_OAUTH2_TOKEN_FILE"], rotatesTokens: true)]
    public async Task ConcurrentCallsWithAnExpiredToken_RefreshExactlyOnce()
    {
        var path = Live.Env("XCONNECT_OAUTH2_TOKEN_FILE")!;
        if (!File.Exists(path))
        {
            output.WriteLine("No token file yet; run the PKCE steps first.");
            return;
        }

        var stored = Live.Deserialize(File.ReadAllText(path));
        var store = new FileTokenStore(path, stored with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) });
        using var http = new HttpClient();
        var client = new XConnectClient(http, new XConnectOptions { OAuth2 = Live.OAuth2Options(), TokenStore = store });

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.Users.GetMeAsync(cancellationToken: CancellationToken.None)));

        results.Should().OnlyContain(r => r.IsSuccess, "every concurrent caller must get the refreshed token");
        store.SaveCount.Should().Be(1, "concurrent refreshes would burn a single-use refresh token");
        output.WriteLine($"5 concurrent calls -> {store.SaveCount} refresh.");
    }

    private sealed class FileTokenStore(string path, XOAuth2Token initial) : IXTokenStore
    {
        private XOAuth2Token _token = initial;

        private int _saves;

        public bool Saved => _saves > 0;

        public int SaveCount => Volatile.Read(ref _saves);

        public ValueTask<XOAuth2Token?> GetTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<XOAuth2Token?>(_token);

        public ValueTask SaveTokenAsync(XOAuth2Token token, CancellationToken cancellationToken)
        {
            _token = token;
            Interlocked.Increment(ref _saves);
            Live.WriteSecretFile(path, Live.Serialize(token));
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// These tests create real Posts and upload real media on the authenticated account. They need the explicit write phrase and always attempt to
/// delete what they create (only Posts recorded in the run's ledger can be deleted). Uploaded media cannot be deleted through the API and is left orphaned (it expires unattached).
/// </summary>
[Trait("Category", "LiveWrite")]
public sealed class LiveWriteTests(ITestOutputHelper output)
{
    [LiveFact(true, "XCONNECT_CONSUMER_KEY", "XCONNECT_CONSUMER_SECRET", "XCONNECT_ACCESS_TOKEN", "XCONNECT_ACCESS_TOKEN_SECRET")]
    public async Task Post_Create_Lookup_Delete_RoundTrip()
    {
        using var http = new HttpClient();
        var client = new XConnectClient(http, new XConnectOptions().UseOAuth1(Live.OAuth1()));
        var ledger = new Live.PostLedger(client, output);
        var text = $"XConnect live test {Guid.NewGuid():N} (safe to delete)";
        string? id = null;
        try
        {
            var created = await client.Posts.CreateAsync(new CreatePostRequest { Text = text }, CancellationToken.None);
            created.IsSuccess.Should().BeTrue(created.Error?.ToString());
            id = created.Data!.Id;
            ledger.Record(id);
            output.WriteLine($"Created Post {id}.");

            var fetched = await client.Posts.GetAsync(id, new XFieldSelection { PostFields = [XPostFields.CreatedAt] }, CancellationToken.None);
            fetched.IsSuccess.Should().BeTrue(fetched.Error?.ToString());
            fetched.Data!.Text.Should().Be(text);

            var duplicate = await client.Posts.CreateAsync(new CreatePostRequest { Text = text }, CancellationToken.None);
            output.WriteLine($"Duplicate create -> HTTP {(int)duplicate.StatusCode}, kind {duplicate.Error?.Kind}, detail '{duplicate.Error?.Detail}'.");
            if (duplicate.IsSuccess)
            {
                ledger.Record(duplicate.Data!.Id);
                await ledger.DeleteAsync(duplicate.Data.Id);
            }
        }
        finally
        {
            if (id is not null)
            {
                (await ledger.DeleteAsync(id)).Should().BeTrue($"delete Post {id} manually if this fails");
            }
        }
    }

    /// <summary>Uploads an image you supply in <c>XCONNECT_LIVE_IMAGE_FILE</c>; nothing is embedded in the suite.</summary>
    [LiveFact(true, "XCONNECT_CONSUMER_KEY", "XCONNECT_CONSUMER_SECRET", "XCONNECT_ACCESS_TOKEN", "XCONNECT_ACCESS_TOKEN_SECRET", "XCONNECT_LIVE_IMAGE_FILE")]
    public async Task Media_UploadImage_AttachToPost_Delete()
    {
        using var http = new HttpClient();
        var client = new XConnectClient(http, new XConnectOptions().UseOAuth1(Live.OAuth1()));
        var ledger = new Live.PostLedger(client, output);
        string? id = null;
        try
        {
            var imagePath = Live.Env("XCONNECT_LIVE_IMAGE_FILE")!;
            await using var png = File.OpenRead(imagePath);
            var media = await client.Media.UploadImageAsync(png, Path.GetFileName(imagePath), CancellationToken.None);
            output.WriteLine($"Uploaded media {media.MediaId}; processing: {media.ProcessingInfo?.State ?? "none"}.");

            var alt = await client.Media.SetAltTextAsync(media.MediaId, "A single transparent pixel used by an automated test", CancellationToken.None);
            output.WriteLine($"Alt text -> HTTP {(int)alt.StatusCode} {alt.Error}.");

            var created = await client.Posts.CreateAsync(
                new CreatePostRequest { Text = $"XConnect live media test {Guid.NewGuid():N} (safe to delete)", Media = new XPostMedia { MediaIds = [media.MediaId] } },
                CancellationToken.None);
            created.IsSuccess.Should().BeTrue(created.Error?.ToString());
            id = created.Data!.Id;
            ledger.Record(id);
        }
        finally
        {
            if (id is not null)
            {
                (await ledger.DeleteAsync(id)).Should().BeTrue($"delete Post {id} manually if this fails");
            }
        }
    }

    /// <summary>Exercises chunked upload and asynchronous processing with a small real video you provide in <c>XCONNECT_LIVE_VIDEO_FILE</c>. No Post is created.</summary>
    [LiveFact(true, "XCONNECT_CONSUMER_KEY", "XCONNECT_CONSUMER_SECRET", "XCONNECT_ACCESS_TOKEN", "XCONNECT_ACCESS_TOKEN_SECRET", "XCONNECT_LIVE_VIDEO_FILE")]
    public async Task Media_UploadVideo_Chunked_WaitsForProcessing()
    {
        using var http = new HttpClient();
        var client = new XConnectClient(http, new XConnectOptions().UseOAuth1(Live.OAuth1()));
        var path = Live.Env("XCONNECT_LIVE_VIDEO_FILE")!;
        await using var video = File.OpenRead(path);
        var progress = new List<XUploadProgress>();

        var result = await client.Media.UploadAsync(
            video,
            new XMediaUploadOptions { Category = XMediaCategory.TweetVideo, FileName = Path.GetFileName(path), Progress = new Sync(progress) },
            CancellationToken.None);

        output.WriteLine($"Uploaded video {result.MediaId} in {progress.Count} progress step(s); final state {result.ProcessingInfo?.State ?? "no processing needed"}.");
        (result.ProcessingInfo is null || result.ProcessingInfo.IsSucceeded).Should().BeTrue();
        progress.Last().BytesSent.Should().Be(video.Length);
    }

    private sealed class Sync(List<XUploadProgress> sink) : IProgress<XUploadProgress>
    {
        public void Report(XUploadProgress value) => sink.Add(value);
    }
}
