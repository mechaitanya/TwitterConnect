using System.Net;
using AwesomeAssertions;
using XConnect.Authentication;
using XConnect.Clients;
using XConnect.Models;

namespace XConnect.Tests;

public sealed class UsersAndEngagementTests
{
    private const string Me = """{"data":{"id":"2244994945","name":"X Dev","username":"xdevelopers"}}""";

    [Fact]
    public async Task GetMe_AcceptsUserContextOnly()
    {
        var client = TestClients.Create(new QueueHandler(), new XConnectOptions().UseBearerToken("app"));

        var act = () => client.Users.GetMeAsync(cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetByUsername_RequestsOnlyChosenFields()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","name":"n","username":"xdevelopers","public_metrics":{"followers_count":10,"following_count":2,"post_count":5,"listed_count":1}}}""");
        var client = TestClients.Create(handler);

        var response = await client.Users.GetByUsernameAsync("xdevelopers", new XFieldSelection { UserFields = [XUserFields.PublicMetrics] }, CancellationToken.None);

        response.Data!.PublicMetrics!.FollowersCount.Should().Be(10);
        handler.Requests.Single().PathAndQuery.Should().Be("/2/users/by/username/xdevelopers?user.fields=public_metrics");
    }

    [Fact]
    public async Task GetMany_JoinsIds()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":[]}""");
        var client = TestClients.Create(handler);

        await client.Users.GetManyAsync(["1", "2", "3"], null, CancellationToken.None);

        handler.Requests.Single().PathAndQuery.Should().Be("/2/users?ids=1%2C2%2C3");
    }

    [Fact]
    public async Task SearchUsers_PagesWithNextToken()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":[],"meta":{"result_count":0}}""");
        var client = TestClients.Create(handler);

        await client.Users.SearchAsync("dotnet", new XPageQuery { MaxResults = 20, PaginationToken = "t" }, CancellationToken.None);

        handler.Requests.Single().PathAndQuery.Should().Be("/2/users/search?query=dotnet&max_results=20&next_token=t");
    }

    [Fact]
    public async Task Followers_UsePaginationToken()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"id":"9","name":"n","username":"u"}],"meta":{"result_count":1,"next_token":"n1"}}""");
        var client = TestClients.Create(handler);

        var page = await client.Users.GetFollowersAsync("5", new XPageQuery { MaxResults = 1000, PaginationToken = "p" }, CancellationToken.None);

        page.NextToken.Should().Be("n1");
        handler.Requests.Single().PathAndQuery.Should().Be("/2/users/5/followers?max_results=1000&pagination_token=p");
    }

    [Fact]
    public async Task Like_ResolvesTheUserOnce_AndPostsTheTweetId()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, Me)
            .Enqueue(HttpStatusCode.OK, """{"data":{"liked":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"liked":false}}""");
        var client = TestClients.Create(handler);

        var like = await client.Engagement.LikeAsync("100", CancellationToken.None);
        var unlike = await client.Engagement.UnlikeAsync("100", CancellationToken.None);

        like.Data!.Liked.Should().BeTrue();
        unlike.Data!.Liked.Should().BeFalse();
        handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").Should().Equal(
            "GET /2/users/me",
            "POST /2/users/2244994945/likes",
            "DELETE /2/users/2244994945/likes/100");
        handler.Requests[1].Body.Should().Be("""{"tweet_id":"100"}""");
    }

    [Fact]
    public async Task ConfiguredUserId_SkipsTheLookup()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"retweeted":true}}""");
        var options = TestClients.OAuth1Options().Also(o => o.AuthenticatedUserId = "42");
        var client = TestClients.Create(handler, options);

        await client.Engagement.RepostAsync("100", CancellationToken.None);

        handler.Requests.Single().PathAndQuery.Should().Be("/2/users/42/retweets");
        handler.Requests.Single().Body.Should().Be("""{"tweet_id":"100"}""");
    }

    [Fact]
    public async Task UndoRepost_UsesTheSourceTweetRoute()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"retweeted":false}}""");
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.AuthenticatedUserId = "42"));

        var response = await client.Engagement.UndoRepostAsync("100", CancellationToken.None);

        response.Data!.Retweeted.Should().BeFalse();
        handler.Requests.Single().PathAndQuery.Should().Be("/2/users/42/retweets/100");
    }

    [Theory]
    [InlineData("follow", "POST", "/2/users/42/following", """{"target_user_id":"7"}""")]
    [InlineData("block", "POST", "/2/users/42/blocking", """{"target_user_id":"7"}""")]
    [InlineData("mute", "POST", "/2/users/42/muting", """{"target_user_id":"7"}""")]
    [InlineData("unfollow", "DELETE", "/2/users/42/following/7", null)]
    [InlineData("unblock", "DELETE", "/2/users/42/blocking/7", null)]
    [InlineData("unmute", "DELETE", "/2/users/42/muting/7", null)]
    public async Task RelationshipChanges_UseTheDocumentedRoutes(string operation, string method, string path, string? body)
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"following":true,"pending_follow":false,"blocking":true,"muting":true}}""");
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.AuthenticatedUserId = "42"));

        switch (operation)
        {
            case "follow": await client.Engagement.FollowAsync("7", CancellationToken.None); break;
            case "block": await client.Engagement.BlockAsync("7", CancellationToken.None); break;
            case "mute": await client.Engagement.MuteAsync("7", CancellationToken.None); break;
            case "unfollow": await client.Engagement.UnfollowAsync("7", CancellationToken.None); break;
            case "unblock": await client.Engagement.UnblockAsync("7", CancellationToken.None); break;
            default: await client.Engagement.UnmuteAsync("7", CancellationToken.None); break;
        }

        var request = handler.Requests.Single();
        request.Method.Method.Should().Be(method);
        request.PathAndQuery.Should().Be(path);
        request.Body.Should().Be(body);
    }

    [Fact]
    public async Task Follow_ReportsPendingRequestsForProtectedAccounts()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"following":false,"pending_follow":true}}""");
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.AuthenticatedUserId = "42"));

        var response = await client.Engagement.FollowAsync("7", CancellationToken.None);

        response.Data!.PendingFollow.Should().BeTrue();
        response.Data.Following.Should().BeFalse();
    }

    [Fact]
    public async Task Bookmarks_RequireOAuth2_AsTheSpecificationStatesForCreation()
    {
        var oauth1Only = TestClients.Create(new QueueHandler(), TestClients.OAuth1Options().Also(o => o.AuthenticatedUserId = "42"));

        var act = () => oauth1Only.Engagement.BookmarkAsync("100", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*OAuth2User*");
    }

    [Fact]
    public async Task Bookmarks_WorkWithOAuth2()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"bookmarked":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"bookmarked":false}}""");
        var options = new XConnectOptions
        {
            AuthenticatedUserId = "42",
            OAuth2 = new XOAuth2Options { ClientId = "c" },
            TokenStore = new InMemoryXTokenStore(new XOAuth2Token { AccessToken = "AT" }),
        };
        var client = TestClients.Create(handler, options);

        var add = await client.Engagement.BookmarkAsync("100", CancellationToken.None);
        var remove = await client.Engagement.RemoveBookmarkAsync("100", CancellationToken.None);

        add.Data!.Bookmarked.Should().BeTrue();
        remove.Data!.Bookmarked.Should().BeFalse();
        handler.Requests[0].Body.Should().Be("""{"tweet_id":"100"}""");
        handler.Requests[1].PathAndQuery.Should().Be("/2/users/42/bookmarks/100");
    }

    [Fact]
    public async Task Lookup_FailureOfTheMeCall_PropagatesAsXApiException()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}""");
        var client = TestClients.Create(handler);

        var act = () => client.Engagement.LikeAsync("1", CancellationToken.None);

        (await act.Should().ThrowAsync<XApiException>()).Which.Error.Kind.Should().Be(XApiErrorKind.Authentication);
        handler.Requests.Should().ContainSingle("the like itself must not be attempted");
    }

    [Fact]
    public async Task EngagementReaders_UseTheirRoutes()
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, """{"data":[]}"""));
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.AuthenticatedUserId = "42"));

        await client.Engagement.GetBookmarksAsync(cancellationToken: CancellationToken.None);
        await client.Engagement.GetBlockedUsersAsync(cancellationToken: CancellationToken.None);
        await client.Engagement.GetMutedUsersAsync(cancellationToken: CancellationToken.None);

        handler.Requests.Select(r => r.PathAndQuery).Should().Equal("/2/users/42/bookmarks", "/2/users/42/blocking", "/2/users/42/muting");
    }
}

public sealed class OtherApiAreaTests
{
    [Fact]
    public async Task DirectMessages_SendToParticipant_WithMedia()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, """{"data":{"dm_conversation_id":"1-2","dm_event_id":"99"}}""");
        var client = TestClients.Create(handler);

        var response = await client.DirectMessages.SendToParticipantAsync("2", new XDirectMessage { Text = "hi", MediaIds = ["m1"] }, CancellationToken.None);

        response.Data!.DmEventId.Should().Be("99");
        var request = handler.Requests.Single();
        request.PathAndQuery.Should().Be("/2/dm_conversations/with/2/messages");
        request.Body.Should().Be("""{"text":"hi","attachments":[{"media_id":"m1"}]}""");
    }

    [Fact]
    public async Task DirectMessages_RejectEmptyMessages()
    {
        var client = TestClients.Create(new QueueHandler());

        var act = () => client.DirectMessages.SendToConversationAsync("c", new XDirectMessage(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DirectMessages_CreateGroup()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, """{"data":{"dm_conversation_id":"g1","dm_event_id":"e1"}}""");
        var client = TestClients.Create(handler);

        await client.DirectMessages.CreateGroupConversationAsync(["1", "2"], new XDirectMessage { Text = "welcome" }, CancellationToken.None);

        handler.Requests.Single().Body.Should().Be("""{"conversation_type":"Group","participant_ids":["1","2"],"message":{"text":"welcome"}}""");
    }

    [Fact]
    public async Task DirectMessages_ReadEventsWithFilters()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK,
            """{"data":[{"id":"1","event_type":"MessageCreate","text":"hey","sender_id":"3","dm_conversation_id":"1-3","created_at":"2026-01-01T00:00:00.000Z"}],"meta":{"result_count":1,"next_token":"nx"}}""");
        var client = TestClients.Create(handler);

        var page = await client.DirectMessages.GetEventsAsync(
            new XDmQuery { MaxResults = 50, EventTypes = ["MessageCreate"], Fields = new XFieldSelection { DmEventFields = ["text", "created_at"] } },
            CancellationToken.None);

        page.Data!.Single().Text.Should().Be("hey");
        page.NextToken.Should().Be("nx");
        handler.Requests.Single().PathAndQuery.Should().Be("/2/dm_events?max_results=50&event_types=MessageCreate&dm_event.fields=text%2Ccreated_at");
    }

    [Fact]
    public async Task DirectMessages_DeleteEvent()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"deleted":true}}""");
        var client = TestClients.Create(handler);

        var response = await client.DirectMessages.DeleteEventAsync("55", CancellationToken.None);

        response.Data!.Deleted.Should().BeTrue();
        handler.Requests.Single().Method.Should().Be(HttpMethod.Delete);
        handler.Requests.Single().PathAndQuery.Should().Be("/2/dm_events/55");
    }

    [Fact]
    public async Task Lists_CreateUpdateDeleteAndMembership()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.Created, """{"data":{"id":"1","name":"Friends"}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"updated":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"is_member":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"is_member":false}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"deleted":true}}""");
        var client = TestClients.Create(handler);

        var created = await client.Lists.CreateAsync(new XListRequest { Name = "Friends", Private = true }, CancellationToken.None);
        await client.Lists.UpdateAsync("1", new XListRequest { Description = "close" }, CancellationToken.None);
        var added = await client.Lists.AddMemberAsync("1", "77", CancellationToken.None);
        var removed = await client.Lists.RemoveMemberAsync("1", "77", CancellationToken.None);
        await client.Lists.DeleteAsync("1", CancellationToken.None);

        created.Data!.Name.Should().Be("Friends");
        added.Data!.IsMember.Should().BeTrue();
        removed.Data!.IsMember.Should().BeFalse();
        handler.Requests.Select(r => $"{r.Method} {r.PathAndQuery}").Should().Equal(
            "POST /2/lists", "PUT /2/lists/1", "POST /2/lists/1/members", "DELETE /2/lists/1/members/77", "DELETE /2/lists/1");
        handler.Requests[0].Body.Should().Be("""{"name":"Friends","private":true}""");
        handler.Requests[1].Body.Should().Be("""{"description":"close"}""");
        handler.Requests[2].Body.Should().Be("""{"user_id":"77"}""");
    }

    [Fact]
    public async Task Lists_CreateRequiresAName()
    {
        var client = TestClients.Create(new QueueHandler());

        var act = () => client.Lists.CreateAsync(new XListRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Lists_FollowUsesTheAuthenticatedUser()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"following":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"following":false}}""");
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.AuthenticatedUserId = "42"));

        await client.Lists.FollowAsync("9", CancellationToken.None);
        await client.Lists.UnfollowAsync("9", CancellationToken.None);

        handler.Requests[0].PathAndQuery.Should().Be("/2/users/42/followed_lists");
        handler.Requests[0].Body.Should().Be("""{"list_id":"9"}""");
        handler.Requests[1].PathAndQuery.Should().Be("/2/users/42/followed_lists/9");
    }

    [Fact]
    public async Task Lists_ReadMembersAndPosts()
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, """{"data":[]}"""));
        var client = TestClients.Create(handler);

        await client.Lists.GetMembersAsync("9", new XPageQuery { MaxResults = 100 }, CancellationToken.None);
        await client.Lists.GetPostsAsync("9", cancellationToken: CancellationToken.None);

        handler.Requests.Select(r => r.PathAndQuery).Should().Equal("/2/lists/9/members?max_results=100", "/2/lists/9/tweets");
    }

    [Fact]
    public async Task Spaces_AcceptBearerOrOAuth2_ButNotOAuth1()
    {
        var oauth1 = TestClients.Create(new QueueHandler());
        var act = () => oauth1.Spaces.GetAsync("1", cancellationToken: CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","state":"live","title":"Talk","participant_count":12}}""");
        var bearer = TestClients.Create(handler, new XConnectOptions().UseBearerToken("app"));
        var space = await bearer.Spaces.GetAsync("1", new XFieldSelection { SpaceFields = ["title", "state"] }, CancellationToken.None);

        space.Data!.Title.Should().Be("Talk");
        handler.Requests.Single().PathAndQuery.Should().Be("/2/spaces/1?space.fields=title%2Cstate");
    }

    [Fact]
    public async Task Spaces_SearchAndLookup()
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, """{"data":[]}"""));
        var client = TestClients.Create(handler, new XConnectOptions().UseBearerToken("app"));

        await client.Spaces.SearchAsync("music", "live", 10, null, CancellationToken.None);
        await client.Spaces.GetByCreatorsAsync(["1", "2"], null, CancellationToken.None);
        await client.Spaces.GetManyAsync(["a", "b"], null, CancellationToken.None);

        handler.Requests.Select(r => r.PathAndQuery).Should().Equal(
            "/2/spaces/search?query=music&state=live&max_results=10",
            "/2/spaces/by/creator_ids?user_ids=1%2C2",
            "/2/spaces?ids=a%2Cb");
    }

    [Fact]
    public async Task Compliance_CreateAndListJobs()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"j1","type":"tweets","status":"created","upload_url":"https://up.example/","upload_expires_at":"2026-01-01T00:00:00.000Z"}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"j1","status":"complete"}]}""");
        var client = TestClients.Create(handler, new XConnectOptions().UseBearerToken("app"));

        var job = await client.Compliance.CreateJobAsync("tweets", "nightly", true, CancellationToken.None);
        var jobs = await client.Compliance.GetJobsAsync("tweets", "complete", CancellationToken.None);

        job.Data!.UploadUrl.Should().Be("https://up.example/");
        jobs.Data!.Single().Status.Should().Be("complete");
        handler.Requests[0].Body.Should().Be("""{"type":"tweets","name":"nightly","resumable":true}""");
        handler.Requests[1].PathAndQuery.Should().Be("/2/compliance/jobs?type=tweets&status=complete");
    }

    [Fact]
    public async Task Compliance_RejectsAnUnknownType()
    {
        var client = TestClients.Create(new QueueHandler(), new XConnectOptions().UseBearerToken("app"));

        var act = () => client.Compliance.CreateJobAsync("likes", cancellationToken: CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Trends_ByWoeid()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"trend_name":"#dotnet","tweet_count":1200}]}""");
        var client = TestClients.Create(handler, new XConnectOptions().UseBearerToken("app"));

        var trends = await client.Trends.GetByWoeidAsync(1, 5, CancellationToken.None);

        trends.Data!.Single().TrendName.Should().Be("#dotnet");
        handler.Requests.Single().PathAndQuery.Should().Be("/2/trends/by/woeid/1?max_trends=5");
    }

    [Fact]
    public async Task Communities_GetAndSearch()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"id":"c1","name":"Devs","member_count":5}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":[{"id":"c1","name":"Devs"}],"meta":{"next_token":"n"}}""");
        var client = TestClients.Create(handler);

        var one = await client.Communities.GetAsync("c1", new XFieldSelection { CommunityFields = ["name", "member_count"] }, CancellationToken.None);
        var search = await client.Communities.SearchAsync("devs", new XPageQuery { MaxResults = 10 }, CancellationToken.None);

        one.Data!.MemberCount.Should().Be(5);
        search.NextToken.Should().Be("n");
        handler.Requests[0].PathAndQuery.Should().Be("/2/communities/c1?community.fields=name%2Cmember_count");
        handler.Requests[1].PathAndQuery.Should().Be("/2/communities/search?query=devs&max_results=10");
    }

    [Fact]
    public async Task PostAnalytics_RequiresIdsAndWindow()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":[{"id":"1","impressions":100,"likes":4}]}""");
        var client = TestClients.Create(handler);

        var response = await client.Posts.GetAnalyticsAsync(
            ["1"],
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 8, 0, 0, 0, TimeSpan.Zero),
            "daily",
            ["impressions", "likes"],
            CancellationToken.None);

        response.Data!.Single().Impressions.Should().Be(100);
        handler.Requests.Single().PathAndQuery.Should().Be(
            "/2/tweets/analytics?ids=1&start_time=2026-01-01T00%3A00%3A00Z&end_time=2026-01-08T00%3A00%3A00Z&granularity=daily&analytics.fields=impressions%2Clikes");
    }
}

public sealed class RouteTableTests
{
    public static TheoryData<string, string, string> Routes => new()
    {
        { "Posts.GetMentions", "GET", "/2/users/7/mentions" },
        { "Posts.GetHomeTimeline", "GET", "/2/users/7/timelines/reverse_chronological" },
        { "Posts.GetLikedPosts", "GET", "/2/users/7/liked_tweets" },
        { "Posts.GetRepostingUsers", "GET", "/2/tweets/7/retweeted_by" },
        { "Posts.GetLikingUsers", "GET", "/2/tweets/7/liking_users" },
        { "Posts.GetQuotePosts", "GET", "/2/tweets/7/quote_tweets" },
        { "Posts.SearchAll", "GET", "/2/tweets/search/all" },
        { "Users.GetById", "GET", "/2/users/7" },
        { "Users.GetByUsernames", "GET", "/2/users/by" },
        { "Users.GetFollowing", "GET", "/2/users/7/following" },
        { "Lists.Get", "GET", "/2/lists/7" },
        { "Lists.GetOwned", "GET", "/2/users/7/owned_lists" },
        { "Lists.GetFollowed", "GET", "/2/users/7/followed_lists" },
        { "DirectMessages.GetConversationEvents", "GET", "/2/dm_conversations/7/dm_events" },
        { "DirectMessages.GetEventsWithParticipant", "GET", "/2/dm_conversations/with/7/dm_events" },
        { "DirectMessages.GetEvent", "GET", "/2/dm_events/7" },
        { "Spaces.GetPosts", "GET", "/2/spaces/7/tweets" },
        { "Compliance.GetJob", "GET", "/2/compliance/jobs/7" },
        { "Media.GetUploadStatus", "GET", "/2/media/upload" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task SupportedReadMethods_CallTheDocumentedRoute(string member, string method, string path)
    {
        var handler = new QueueHandler(_ => QueueHandler.Json(HttpStatusCode.OK, """{"data":[]}"""));
        // Spaces and a few other endpoints do not accept OAuth 1.0a, so register every method; the transport picks what each endpoint accepts.
        var options = new XConnectOptions().UseBearerToken("app").UseOAuth1(TestClients.Credentials);
        options.OAuth2 = new XOAuth2Options { ClientId = "c" };
        options.TokenStore = new XConnect.Authentication.InMemoryXTokenStore(new XConnect.Authentication.XOAuth2Token { AccessToken = "AT" });
        var client = TestClients.Create(handler, options);
        var ct = CancellationToken.None;

        _ = member switch
        {
            "Posts.GetMentions" => await client.Posts.GetMentionsAsync("7", null, ct) is { },
            "Posts.GetHomeTimeline" => await client.Posts.GetHomeTimelineAsync("7", null, ct) is { },
            "Posts.GetLikedPosts" => await client.Posts.GetLikedPostsAsync("7", null, ct) is { },
            "Posts.GetRepostingUsers" => await client.Posts.GetRepostingUsersAsync("7", null, ct) is { },
            "Posts.GetLikingUsers" => await client.Posts.GetLikingUsersAsync("7", null, ct) is { },
            "Posts.GetQuotePosts" => await client.Posts.GetQuotePostsAsync("7", null, ct) is { },
            "Posts.SearchAll" => await client.Posts.SearchAllAsync("q", null, ct) is { },
            "Users.GetById" => await client.Users.GetByIdAsync("7", null, ct) is { },
            "Users.GetByUsernames" => await client.Users.GetByUsernamesAsync(["a"], null, ct) is { },
            "Users.GetFollowing" => await client.Users.GetFollowingAsync("7", null, ct) is { },
            "Lists.Get" => await client.Lists.GetAsync("7", null, ct) is { },
            "Lists.GetOwned" => await client.Lists.GetOwnedAsync("7", null, ct) is { },
            "Lists.GetFollowed" => await client.Lists.GetFollowedAsync("7", null, ct) is { },
            "DirectMessages.GetConversationEvents" => await client.DirectMessages.GetConversationEventsAsync("7", null, ct) is { },
            "DirectMessages.GetEventsWithParticipant" => await client.DirectMessages.GetEventsWithParticipantAsync("7", null, ct) is { },
            "DirectMessages.GetEvent" => await client.DirectMessages.GetEventAsync("7", null, ct) is { },
            "Spaces.GetPosts" => await client.Spaces.GetPostsAsync("7", null, ct) is { },
            "Compliance.GetJob" => await client.Compliance.GetJobAsync("7", ct) is { },
            _ => await client.Media.GetUploadStatusAsync("7", ct) is { },
        };

        var request = handler.Requests.Single();
        request.Method.Method.Should().Be(method);
        request.Uri.AbsolutePath.Should().Be(path);
        request.Authorization.Should().NotBeNullOrEmpty();
    }
}
