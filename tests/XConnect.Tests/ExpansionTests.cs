using System.Net;
using System.Text;
using AwesomeAssertions;
using XConnect.Authentication;
using XConnect.Models;
using XConnect.Streaming;

namespace XConnect.Tests;

public sealed class ErrorModelTests
{
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "https://api.twitter.com/2/problems/usage-capped", XApiErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.TooManyRequests, "https://api.twitter.com/2/problems/rate-limit-exceeded", XApiErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.Forbidden, "https://api.twitter.com/2/problems/client-forbidden", XApiErrorKind.Authorization)]
    [InlineData(HttpStatusCode.Forbidden, "https://api.twitter.com/2/problems/not-authorized-for-resource", XApiErrorKind.Authorization)]
    [InlineData(HttpStatusCode.BadRequest, "https://api.twitter.com/2/problems/invalid-rules", XApiErrorKind.Validation)]
    [InlineData(HttpStatusCode.BadRequest, "https://api.twitter.com/2/problems/duplicate-rules", XApiErrorKind.Validation)]
    [InlineData(HttpStatusCode.Conflict, "https://api.twitter.com/2/problems/streaming-connection", XApiErrorKind.Conflict)]
    [InlineData(HttpStatusCode.OK, "https://api.twitter.com/2/problems/resource-not-found", XApiErrorKind.NotFound)]
    public async Task DocumentedProblemTypes_TakePrecedenceOverTheStatus(HttpStatusCode status, string type, XApiErrorKind expected)
    {
        var handler = new QueueHandler().Enqueue(status == HttpStatusCode.OK ? HttpStatusCode.BadRequest : status, $$"""{"title":"t","detail":"d","type":"{{type}}"}""");
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.Retry = new XRetryOptions { MaxRetries = 0 }));

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        response.Error!.Kind.Should().Be(expected);
    }

    [Fact]
    public async Task Status409_WithoutAType_IsAConflict()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Conflict, """{"title":"ConnectionException","detail":"This stream is currently at the maximum allowed connection limit."}""");
        var client = TestClients.Create(handler, new XConnectOptions().UseBearerToken("app"));

        var act = async () => await client.Streaming.StreamFilteredAsync(cancellationToken: CancellationToken.None).GetAsyncEnumerator().MoveNextAsync();

        (await act.Should().ThrowAsync<XApiException>()).Which.Error.Kind.Should().Be(XApiErrorKind.Conflict);
        handler.Requests.Should().ContainSingle("a 409 will not heal by reconnecting");
    }

    [Fact]
    public async Task NoContentSuccess_IsNotAnError()
    {
        var handler = new QueueHandler().Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = TestClients.Create(handler);

        var response = await client.Posts.DeleteAsync("1", CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeNull();
    }

    [Fact]
    public void XApiErrorKind_ExistingValuesAreStable()
    {
        ((int)XApiErrorKind.Unknown).Should().Be(0);
        ((int)XApiErrorKind.Serialization).Should().Be(8, "new members must be appended so persisted values keep their meaning");
        ((int)XApiErrorKind.Conflict).Should().Be(9);
    }
}

public sealed class PostsCompletenessTests
{
    [Fact]
    public async Task Create_WithMediaExtrasAndCallToAction_SerializesDocumentedFields()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, """{"data":{"id":"1","text":"v"}}""");
        var client = TestClients.Create(handler);

        await client.Posts.CreateAsync(
            new CreatePostRequest
            {
                Text = "v",
                Media = new XPostMedia
                {
                    MediaIds = ["m"],
                    Title = "Launch",
                    Description = "Our launch",
                    Embeddable = false,
                    PreviewMediaId = "p",
                    CallToActions = new XPostCallToActions { VisitSite = new XUrlCallToAction { Url = "https://example.com/go" } },
                },
            },
            CancellationToken.None);

        handler.Requests.Single().Body.Should().Be(
            """{"text":"v","media":{"media_ids":["m"],"title":"Launch","description":"Our launch","embeddable":false,"preview_media_id":"p","call_to_actions":{"visit_site":{"url":"https://example.com/go"}}}}""");
    }

    [Fact]
    public async Task Create_AppInstallCallToAction_UsesStoreIds()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, """{"data":{"id":"1","text":"v"}}""");
        var client = TestClients.Create(handler);

        await client.Posts.CreateAsync(
            new CreatePostRequest
            {
                Media = new XPostMedia
                {
                    MediaIds = ["m"],
                    CallToActions = new XPostCallToActions { AppInstall = new XAppInstallCallToAction { AppStoreId = "1", IpadAppStoreId = "2", PlayStoreId = "com.x" } },
                },
            },
            CancellationToken.None);

        handler.Requests.Single().Body.Should().Contain("""{"app_install":{"app_store_id":"1","ipad_app_store_id":"2","play_store_id":"com.x"}}""");
    }

    public static TheoryData<XPostCallToActions> BadCallToActions => new()
    {
        new XPostCallToActions(),
        new XPostCallToActions { VisitSite = new XUrlCallToAction { Url = "https://a.test" }, WatchNow = new XUrlCallToAction { Url = "https://b.test" } },
        new XPostCallToActions { VisitSite = new XUrlCallToAction { Url = "http://insecure.test" } },
        new XPostCallToActions { WatchNow = new XUrlCallToAction { Url = "not a url" } },
    };

    [Theory]
    [MemberData(nameof(BadCallToActions))]
    public async Task Create_InvalidCallToAction_IsRejectedLocally(XPostCallToActions cta)
    {
        var handler = new QueueHandler();
        var client = TestClients.Create(handler);

        var act = () => client.Posts.CreateAsync(new CreatePostRequest { Media = new XPostMedia { MediaIds = ["m"], CallToActions = cta } }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_PollOnlyWithoutText_IsRejected_BecauseTheApiRequiresTextOrMedia()
    {
        var client = TestClients.Create(new QueueHandler());

        var act = () => client.Posts.CreateAsync(new CreatePostRequest { Poll = new XPostPoll { Options = ["a", "b"], DurationMinutes = 60 } }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetReposts_AndRepostsOfMe_UseTheirRoutes()
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, """{"data":[{"id":"9","text":"RT"}],"meta":{"result_count":1,"next_token":"n"}}"""));
        var client = TestClients.Create(handler);

        var reposts = await client.Posts.GetRepostsAsync("5", new XPageQuery { MaxResults = 10 }, CancellationToken.None);
        var mine = await client.Posts.GetRepostsOfMeAsync(new XPageQuery { PaginationToken = "p" }, CancellationToken.None);

        reposts.Data!.Single().Id.Should().Be("9");
        mine.NextToken.Should().Be("n");
        handler.Requests.Select(r => r.PathAndQuery).Should().Equal("/2/tweets/5/retweets?max_results=10", "/2/users/reposts_of_me?pagination_token=p");
    }

    [Fact]
    public async Task PostAnalytics_ParsesTotalsAndTimeBuckets()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """
            {"data":[{"id":"1","timestamp":"2026-01-01T00:00:00Z","impressions":100,"engagements":7,"url_clicks":2,
              "timestamped_metrics":[{"timestamp":"2026-01-01T00:00:00Z","metrics":{"impressions":60,"likes":3,"unlikes":null}},{"timestamp":"2026-01-02T00:00:00Z","metrics":{"impressions":40}}]}]}
            """);
        var client = TestClients.Create(handler);

        var response = await client.Posts.GetAnalyticsAsync(["1"], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(2), "daily", ["impressions", "timestamped_metrics"], CancellationToken.None);

        var item = response.Data!.Single();
        item.Impressions.Should().Be(100);
        item.UrlClicks.Should().Be(2);
        item.TimestampedMetrics!.Select(b => b.Metrics!.Impressions).Should().Equal(60, 40);
        item.TimestampedMetrics![0].Metrics!.Likes.Should().Be(3);
        item.TimestampedMetrics![0].Metrics!.Unlikes.Should().BeNull();
    }
}

public sealed class MediaCompletenessTests
{
    private static MemoryStream Text(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task SetMetadata_SendsOnlyWhatWasSet()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"7"}}""");
        var client = TestClients.Create(handler);

        await client.Media.SetMetadataAsync(
            "7",
            new XMediaMetadata
            {
                AltText = "alt",
                AllowDownload = false,
                ContentExpirationTimestampSec = 1893456000,
                DomainWhitelist = ["example.com"],
                FoundMediaOrigin = new XFoundMediaOrigin { Provider = "giphy", Id = "abc" },
                Managed = true,
                Shared = true,
                UploadSource = "cms",
            },
            CancellationToken.None);

        handler.Requests.Single().Body.Should().Be(
            """{"id":"7","metadata":{"alt_text":{"text":"alt"},"allow_download_status":{"allow_download":false},"content_expiration":{"timestamp_sec":1893456000},"domain_restrictions":{"whitelist":["example.com"]},"found_media_origin":{"provider":"giphy","id":"abc"},"management_info":{"managed":true},"shared_info":{"shared":true},"upload_source":{"upload_source":"cms"}}}""");
    }

    [Fact]
    public async Task SetMetadata_Validates()
    {
        var client = TestClients.Create(new QueueHandler());

        await client.Media.Invoking(m => m.SetMetadataAsync("7", new XMediaMetadata(), CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await client.Media.Invoking(m => m.SetAltTextAsync("7", new string('a', 1001), CancellationToken.None)).Should().ThrowAsync<ArgumentException>("the API limits alt text to 1000 characters");
        await client.Media.Invoking(m => m.SetAltTextAsync("7", new string('a', 1000), CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>("1000 characters is valid, so it proceeds to the (unstubbed) request");
    }

    [Fact]
    public async Task SubtitleFile_IsUploadedWithTheSubtitlesCategory()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"88"}}""");
        var client = TestClients.Create(handler);
        await using var srt = Text("1\n00:00:00,000 --> 00:00:01,000\nHi\n");

        var result = await client.Media.UploadAsync(srt, new XMediaUploadOptions { Category = XMediaCategory.Subtitles, FileName = "en.srt" }, CancellationToken.None);

        result.MediaId.Should().Be("88");
        var body = Encoding.Latin1.GetString(handler.Requests.Single().BodyBytes!);
        body.Should().Contain("subtitles").And.Contain("Content-Type: text/srt");
    }

    [Fact]
    public async Task SubtitleFile_RejectsOtherTypesAndLargeFiles()
    {
        var client = TestClients.Create(new QueueHandler());
        await using var png = Text("x");
        await using var big = new MemoryStream(new byte[(1024 * 1024) + 1]);

        await client.Media.Invoking(m => m.UploadAsync(png, new XMediaUploadOptions { Category = XMediaCategory.Subtitles, FileName = "a.png" }, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*not supported*");
        await client.Media.Invoking(m => m.UploadAsync(big, new XMediaUploadOptions { Category = XMediaCategory.Subtitles, FileName = "a.vtt" }, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*limit*");
    }

    [Fact]
    public async Task Subtitles_CreateAndDelete()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"5","media_category":"TweetVideo","associated_subtitles":[{"language_code":"en"}]}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"deleted":true}}""");
        var client = TestClients.Create(handler);

        var created = await client.Media.CreateSubtitlesAsync("5", XSubtitleCategory.TweetVideo, "88", "en", "English", CancellationToken.None);
        var deleted = await client.Media.DeleteSubtitlesAsync("5", XSubtitleCategory.TweetVideo, "en", CancellationToken.None);

        created.Data!.MediaCategory.Should().Be("TweetVideo");
        deleted.Data!.Deleted.Should().BeTrue();
        handler.Requests[0].Body.Should().Be("""{"id":"5","media_category":"TweetVideo","subtitles":{"display_name":"English","id":"88","language_code":"en"}}""");
        handler.Requests[1].Method.Should().Be(HttpMethod.Delete);
        handler.Requests[1].Body.Should().Be("""{"id":"5","language_code":"en","media_category":"TweetVideo"}""");
    }

    [Fact]
    public async Task MediaLookup_ByKeyAndByKeys()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"media_key":"3_1","type":"photo","width":10,"height":20,"alt_text":"a"}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"media_key":"3_1","type":"photo"},{"media_key":"13_2","type":"video","variants":[{"bit_rate":832000,"content_type":"video/mp4","url":"https://v"}]}]}""");
        var client = TestClients.Create(handler, new XConnectOptions().UseBearerToken("app"));

        var one = await client.Media.GetAsync("3_1", new XFieldSelection { MediaFields = [XMediaFields.AltText, XMediaFields.Width] }, CancellationToken.None);
        var many = await client.Media.GetManyAsync(["3_1", "13_2"], null, CancellationToken.None);

        one.Data!.AltText.Should().Be("a");
        many.Data![1].Variants!.Single().BitRate.Should().Be(832000);
        handler.Requests[0].PathAndQuery.Should().Be("/2/media/3_1?media.fields=alt_text%2Cwidth");
        handler.Requests[1].PathAndQuery.Should().Be("/2/media?media_keys=3_1%2C13_2");
    }

    [Fact]
    public async Task MediaAnalytics_ParsesMixedTypeCounters()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """
            {"data":[{"media_key":"13_2","video_views":120,"watch_time_ms":"98000","timestamped_metrics":[{"timestamp":"2026-01-01T00:00:00Z","metrics":{"video_views":50,"playback_start":"n/a"}}]}]}
            """);
        var client = TestClients.Create(handler);

        var response = await client.Media.GetAnalyticsAsync(["13_2"], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), "daily", ["video_views", "watch_time_ms"], CancellationToken.None);

        var item = response.Data!.Single();
        item.VideoViews!.Value.GetInt32().Should().Be(120);
        item.WatchTimeMs!.Value.GetString().Should().Be("98000");
        item.TimestampedMetrics![0].Metrics!.PlaybackStart!.Value.GetString().Should().Be("n/a");
        handler.Requests.Single().PathAndQuery.Should().Contain("media_analytics.fields=video_views%2Cwatch_time_ms").And.Contain("granularity=daily");
    }
}

public sealed class RemainingAreasTests
{
    private static XConnectClient Client(QueueHandler handler, string? userId = "42") =>
        TestClients.Create(handler, new XConnectOptions { AuthenticatedUserId = userId, OAuth2 = new XOAuth2Options { ClientId = "c" }, TokenStore = new InMemoryXTokenStore(new XOAuth2Token { AccessToken = "AT" }) }
            .UseBearerToken("app").UseOAuth1(TestClients.Credentials));

    [Fact]
    public async Task Users_Affiliates()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"id":"1","name":"n","username":"u"}],"meta":{"result_count":1}}""");

        var response = await Client(handler).Users.GetAffiliatesAsync("9", new XPageQuery { MaxResults = 5 }, CancellationToken.None);

        response.Data!.Single().Username.Should().Be("u");
        handler.Requests.Single().PathAndQuery.Should().Be("/2/users/9/affiliates?max_results=5");
    }

    [Fact]
    public async Task BookmarkFolders_ListCreateAndRead()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"f1","name":"Read later"}]}""")
            .Enqueue(HttpStatusCode.Created, """{"data":{"id":"f2","name":"Ideas"}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"100"},{"id":"101"}]}""");
        var client = Client(handler);

        var folders = await client.Engagement.GetBookmarkFoldersAsync(cancellationToken: CancellationToken.None);
        var created = await client.Engagement.CreateBookmarkFolderAsync("Ideas", CancellationToken.None);
        var posts = await client.Engagement.GetBookmarksInFolderAsync("f2", cancellationToken: CancellationToken.None);

        folders.Data!.Single().Name.Should().Be("Read later");
        created.Data!.Id.Should().Be("f2");
        posts.Data!.Select(p => p.Id).Should().Equal("100", "101");
        handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").Should().Equal(
            "GET /2/users/42/bookmarks/folders", "POST /2/users/42/bookmarks/folders", "GET /2/users/42/bookmarks/folders/f2");
        handler.Requests[1].Body.Should().Be("""{"name":"Ideas"}""");
        handler.Requests[1].Authorization.Should().Be("Bearer AT", "folder creation accepts OAuth 2.0 only");
    }

    [Fact]
    public async Task BookmarkFolderCreation_RefusesOAuth1()
    {
        var client = TestClients.Create(new QueueHandler(), TestClients.OAuth1Options().Also(o => o.AuthenticatedUserId = "42"));

        var act = () => client.Engagement.CreateBookmarkFolderAsync("x", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Lists_FollowersMembershipsAndPinning()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":[]}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"l1","name":"L"}]}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"l1","name":"L"}],"meta":{"result_count":1}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"pinned":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"pinned":false}}""");
        var client = Client(handler);

        await client.Lists.GetFollowersAsync("l1", new XPageQuery { MaxResults = 100 }, CancellationToken.None);
        var memberships = await client.Lists.GetMembershipsAsync("7", cancellationToken: CancellationToken.None);
        var pinned = await client.Lists.GetPinnedAsync(cancellationToken: CancellationToken.None);
        var pin = await client.Lists.PinAsync("l1", CancellationToken.None);
        var unpin = await client.Lists.UnpinAsync("l1", CancellationToken.None);

        memberships.Data!.Single().Id.Should().Be("l1");
        pinned.Data!.Single().Name.Should().Be("L");
        pin.Data!.Pinned.Should().BeTrue();
        unpin.Data!.Pinned.Should().BeFalse();
        handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").Should().Equal(
            "GET /2/lists/l1/followers?max_results=100", "GET /2/users/7/list_memberships", "GET /2/users/42/pinned_lists", "POST /2/users/42/pinned_lists", "DELETE /2/users/42/pinned_lists/l1");
        handler.Requests[3].Body.Should().Be("""{"list_id":"l1"}""");
    }

    [Fact]
    public async Task DirectMessages_BlockAndUnblockSenders()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"blocked":true}}""").Enqueue(HttpStatusCode.OK, """{"data":{"blocked":false}}""");
        var client = Client(handler);

        var blocked = await client.DirectMessages.BlockSenderAsync("9", CancellationToken.None);
        var unblocked = await client.DirectMessages.UnblockSenderAsync("9", CancellationToken.None);

        blocked.Data!.Blocked.Should().BeTrue();
        unblocked.Data!.Blocked.Should().BeFalse();
        handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").Should().Equal("POST /2/users/9/dm/block", "POST /2/users/9/dm/unblock");
        handler.Requests[0].BodyBytes.Should().BeNull();
    }

    [Fact]
    public async Task Trends_Personalized()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"trend_name":"#dotnet","category":"Tech","post_count":"1.2K","trending_since":"2 hours ago"}]}""");

        var response = await Client(handler).Trends.GetPersonalizedAsync(["trend_name", "post_count"], CancellationToken.None);

        response.Data!.Single().PostCount.Should().Be("1.2K");
        handler.Requests.Single().PathAndQuery.Should().Be("/2/users/personalized_trends?personalized_trend.fields=trend_name%2Cpost_count");
    }

    [Fact]
    public async Task News_SearchAndGet()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"n1","name":"Headline","hook":"h","updated_at":"2026-01-01T00:00:00.000Z"}],"meta":{"result_count":1}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"n1","summary":"s","cluster_posts_results":[{"post_id":"7"}],"contexts":{"topics":["a"]},"keywords":{"x":1}}}""");
        var client = Client(handler);

        var search = await client.News.SearchAsync("dotnet", 5, 24, ["name", "hook"], CancellationToken.None);
        var story = await client.News.GetAsync("n1", ["summary", "cluster_posts_results"], CancellationToken.None);

        search.Data!.Single().Name.Should().Be("Headline");
        story.Data!.ClusterPostsResults!.Single().PostId.Should().Be("7");
        story.Data.Contexts.Should().NotBeNull();
        handler.Requests[0].PathAndQuery.Should().Be("/2/news/search?query=dotnet&max_results=5&max_age_hours=24&news.fields=name%2Chook");
        handler.Requests[1].PathAndQuery.Should().Be("/2/news/n1?news.fields=summary%2Ccluster_posts_results");
    }

    [Fact]
    public async Task Usage_PostUsageAndCredits()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"project_id":"p","project_cap":"1000000","project_usage":"250","cap_reset_day":12,"daily_project_usage":{"project_id":"p","usage":[]}}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"total_balance":10.5,"prepaid_balance":5.5,"free_balance":5,"free_grants":[{"amount":5,"expires_at":"2026-12-31"}]}}""");
        var client = Client(handler);

        var usage = await client.Usage.GetPostUsageAsync(7, ["project_cap", "project_usage"], CancellationToken.None);
        var credits = await client.Usage.GetCreditsAsync(CancellationToken.None);

        usage.Data!.CapResetDay.Should().Be(12);
        usage.Data.ProjectCap.Should().Be("1000000");
        credits.Data!.TotalBalance.Should().Be(10.5);
        handler.Requests[0].PathAndQuery.Should().Be("/2/usage/tweets?days=7&usage.fields=project_cap%2Cproject_usage");
        handler.Requests[0].Authorization.Should().Be("Bearer app", "post usage accepts only app-only authentication");
        handler.Requests[1].PathAndQuery.Should().Be("/2/usage/credits");
    }

    [Fact]
    public async Task Streaming_RuleCounts()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"cap_per_client_app":"5","cap_per_project":"25","project_rules_count":"3","client_app_rules_count":{"client_app_id":"1","rule_count":2},"all_project_client_apps":[]}}""");

        var response = await Client(handler).Streaming.GetRuleCountsAsync(CancellationToken.None);

        response.Data!.CapPerProject.Should().Be("25");
        response.Data.ClientAppRulesCount.Should().NotBeNull();
        handler.Requests.Single().PathAndQuery.Should().Be("/2/tweets/search/stream/rules/counts");
    }

    [Theory]
    [InlineData(XPostStreamKind.Sample10, "/2/tweets/sample10/stream")]
    [InlineData(XPostStreamKind.Firehose, "/2/tweets/firehose/stream")]
    [InlineData(XPostStreamKind.FirehoseEnglish, "/2/tweets/firehose/stream/lang/en")]
    [InlineData(XPostStreamKind.FirehoseJapanese, "/2/tweets/firehose/stream/lang/ja")]
    [InlineData(XPostStreamKind.FirehoseKorean, "/2/tweets/firehose/stream/lang/ko")]
    [InlineData(XPostStreamKind.FirehosePortuguese, "/2/tweets/firehose/stream/lang/pt")]
    public async Task PartitionedStreams_SendThePartition(XPostStreamKind kind, string route)
    {
        var line = """{"data":{"id":"1","text":"t"}}""" + "\r\n";
        var handler = new QueueHandler().Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(line) });
        var client = Client(handler);

        await using var e = client.Streaming.StreamPartitionAsync(kind, 2, new XStreamOptions { BackfillMinutes = 2 }, CancellationToken.None).GetAsyncEnumerator();
        (await e.MoveNextAsync()).Should().BeTrue();

        handler.Requests.First().PathAndQuery.Should().Be($"{route}?backfill_minutes=2&partition=2");
        handler.Requests.First().Authorization.Should().Be("Bearer app");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PartitionedStream_RejectsPartitionsBelowOne_AsTheSpecificationRequires(int partition)
    {
        var handler = new QueueHandler();
        var client = Client(handler);

        var act = () => client.Streaming.StreamPartitionAsync(XPostStreamKind.Firehose, partition, cancellationToken: CancellationToken.None);

        act.Should().Throw<ArgumentOutOfRangeException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PostStreams_UseTheSpecificationsTweetFieldsName_WhileOtherEndpointsUsePostFields()
    {
        var line = """{"data":{"id":"1","text":"t"}}""" + "\r\n";
        var handler = new QueueHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(line) })
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","text":"t"}}""");
        var client = Client(handler);
        var fields = new XFieldSelection { PostFields = [XPostFields.CreatedAt], Expansions = ["author_id"] };

        await using var e = client.Streaming.StreamSampleAsync(new XStreamOptions { Fields = fields }, CancellationToken.None).GetAsyncEnumerator();
        (await e.MoveNextAsync()).Should().BeTrue();
        await client.Posts.GetAsync("1", fields, CancellationToken.None);

        handler.Requests[0].PathAndQuery.Should().Contain("tweet.fields=created_at").And.NotContain("post.fields");
        handler.Requests[1].PathAndQuery.Should().Contain("post.fields=created_at").And.NotContain("tweet.fields");
    }

    [Fact]
    public async Task PartitionedStream_ForbiddenWithoutEnterpriseAccess_FailsFast()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Forbidden, """{"title":"Forbidden","type":"https://api.twitter.com/2/problems/client-forbidden","detail":"Your app is not enrolled for this endpoint."}""");
        var client = Client(handler);

        var act = async () => await client.Streaming.StreamPartitionAsync(XPostStreamKind.Firehose, 1, cancellationToken: CancellationToken.None).GetAsyncEnumerator().MoveNextAsync();

        (await act.Should().ThrowAsync<XApiException>()).Which.Error.Kind.Should().Be(XApiErrorKind.Authorization);
    }

    [Fact]
    public async Task Compliance_Cancel()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"j","status":"failed"}}""");

        var response = await Client(handler).Compliance.CancelJobAsync("j", CancellationToken.None);

        response.Data!.Id.Should().Be("j");
        handler.Requests.Single().Method.Should().Be(HttpMethod.Delete);
        handler.Requests.Single().PathAndQuery.Should().Be("/2/compliance/jobs/j");
    }

    [Fact]
    public async Task Compliance_UploadAndDownload_UsePresignedUrlsWithoutCredentials()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, "{}")
            .Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"1\",\"action\":\"delete\",\"reason\":\"deleted\"}\n") });
        var client = Client(handler);
        await using var ids = new MemoryStream(Encoding.UTF8.GetBytes("1\n2\n"));

        await client.Compliance.UploadIdsAsync(new Uri("https://storage.example.test/upload?sig=abc"), ids, CancellationToken.None);
        await using var results = await client.Compliance.DownloadResultsAsync(new Uri("https://storage.example.test/download?sig=def"), CancellationToken.None);
        using var reader = new StreamReader(results);

        (await reader.ReadToEndAsync()).Should().Contain("\"action\":\"delete\"");
        handler.Requests[0].Method.Should().Be(HttpMethod.Put);
        handler.Requests[0].Uri.Query.Should().Be("?sig=abc");
        handler.Requests[0].Message.Content!.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
        handler.Requests.Should().AllSatisfy(r => r.Authorization.Should().BeNull("pre-signed URLs carry their own authorisation, and our credentials must not leak to another host"));
    }

    [Fact]
    public async Task Compliance_PresignedUrls_MustBeHttps_AndFailuresSurface()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Forbidden, "{}");
        var client = Client(handler);
        await using var ids = new MemoryStream([1]);

        await client.Compliance.Invoking(c => c.UploadIdsAsync(new Uri("http://insecure.test/u"), ids, CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await client.Compliance.Invoking(c => c.DownloadResultsAsync(new Uri("ftp://x/y"), CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await client.Compliance.Invoking(c => c.UploadIdsAsync(new Uri("https://storage.example.test/u"), ids, CancellationToken.None)).Should().ThrowAsync<HttpRequestException>();
    }
}

public sealed class ConcurrencyTests
{
    [Fact]
    public void RateLimitTracker_KeepsTheNewestWindowAndTheLowestRemaining()
    {
        var tracker = new XRateLimitTracker();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 15, 0, TimeSpan.Zero);

        tracker.Record(HttpMethod.Get, "/r", new XRateLimit(100, 90, t0));
        tracker.Record(HttpMethod.Get, "/r", new XRateLimit(100, 95, t0));   // an older response finishing late: ignored
        tracker.TryGet(HttpMethod.Get, "/r", out var a);
        a!.Remaining.Should().Be(90);

        tracker.Record(HttpMethod.Get, "/r", new XRateLimit(100, 80, t0));   // newer in the same window: lower wins
        tracker.TryGet(HttpMethod.Get, "/r", out var b);
        b!.Remaining.Should().Be(80);

        tracker.Record(HttpMethod.Get, "/r", new XRateLimit(100, 99, t0.AddMinutes(15))); // a new window replaces the old one
        tracker.TryGet(HttpMethod.Get, "/r", out var c);
        c!.Remaining.Should().Be(99);

        tracker.Record(HttpMethod.Get, "/r", new XRateLimit(100, 1, t0));    // a straggler from the previous window: ignored
        tracker.TryGet(HttpMethod.Get, "/r", out var d);
        d!.Remaining.Should().Be(99);
    }

    [Fact]
    public async Task RateLimitTracker_IsConsistentUnderParallelUpdates()
    {
        var tracker = new XRateLimitTracker();
        var reset = new DateTimeOffset(2026, 1, 1, 0, 15, 0, TimeSpan.Zero);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() => tracker.Record(HttpMethod.Post, "/2/tweets", new XRateLimit(200, 199 - i, reset)))));

        tracker.TryGet(HttpMethod.Post, "/2/tweets", out var limit);
        limit!.Remaining.Should().Be(0, "the lowest remaining value across the window is the truth, whatever the arrival order");
    }

    [Fact]
    public async Task MaxConcurrentRequests_CapsRequestsInFlight()
    {
        var inFlight = 0;
        var peak = 0;
        var handler = new QueueHandler(_ =>
        {
            var now = Interlocked.Increment(ref inFlight);
            int seen;
            while (now > (seen = Volatile.Read(ref peak)) && Interlocked.CompareExchange(ref peak, now, seen) != seen)
            {
            }

            Thread.Sleep(30);
            Interlocked.Decrement(ref inFlight);
            return QueueHandler.Json(HttpStatusCode.OK, """{"data":{"id":"1","text":"t"}}""");
        });
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.MaxConcurrentRequests = 3));

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => client.Posts.GetAsync("1", cancellationToken: CancellationToken.None))));

        peak.Should().BeLessThanOrEqualTo(3);
        handler.Requests.Should().HaveCount(20);
    }

    [Fact]
    public async Task MaxConcurrentRequests_ReleasesTheSlotOnFailure_SoNothingDeadlocks()
    {
        var handler = new QueueHandler(_ => throw new HttpRequestException("boom"));
        var options = TestClients.OAuth1Options().Also(o =>
        {
            o.MaxConcurrentRequests = 1;
            o.Retry = new XRetryOptions { MaxRetries = 0 };
        });
        var client = TestClients.Create(handler, options);

        for (var i = 0; i < 5; i++)
        {
            await client.Posts.Invoking(p => p.GetAsync("1", cancellationToken: CancellationToken.None)).Should().ThrowAsync<HttpRequestException>();
        }

        handler.Requests.Should().HaveCount(5);
    }

    [Fact]
    public async Task InvalidConcurrencyLimit_IsRejected()
    {
        var options = TestClients.OAuth1Options().Also(o => o.MaxConcurrentRequests = 0);

        var act = () => new XConnectClient(new HttpClient(new QueueHandler()), options);

        act.Should().Throw<ArgumentException>().WithMessage("*MaxConcurrentRequests*");
        await Task.CompletedTask;
    }
}
