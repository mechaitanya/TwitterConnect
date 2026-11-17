using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using XConnect.Models;

namespace XConnect.Tests;

public sealed class PostsClientTests
{
    private const string Created = """{"data":{"id":"1445880548472328192","text":"Hello"}}""";

    [Fact]
    public async Task Create_PlainPost_SendsMinimalJsonAndSignsWithOAuth1()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, Created);
        var client = TestClients.Create(handler);

        var response = await client.Posts.CreateAsync(new CreatePostRequest { Text = "Hello" }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Id.Should().Be("1445880548472328192");
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be(new Uri("https://api.x.com/2/tweets"));
        request.Body.Should().Be("""{"text":"Hello"}""");
        request.Authorization.Should().StartWith("OAuth ");
    }

    [Fact]
    public async Task Create_Reply_NestsInReplyToTweetId()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, Created);
        var client = TestClients.Create(handler);

        await client.Posts.ReplyAsync("123", "me too", CancellationToken.None);

        handler.Requests.Single().Body.Should().Be("""{"text":"me too","reply":{"in_reply_to_tweet_id":"123"}}""");
    }

    [Fact]
    public async Task Create_Quote_SendsQuoteTweetId()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, Created);
        var client = TestClients.Create(handler);

        await client.Posts.QuoteAsync("456", "look at this", CancellationToken.None);

        handler.Requests.Single().Body.Should().Be("""{"text":"look at this","quote_tweet_id":"456"}""");
    }

    [Fact]
    public async Task Create_PollWithSettings_SerializesSnakeCase()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, Created);
        var client = TestClients.Create(handler);

        await client.Posts.CreateAsync(
            new CreatePostRequest
            {
                Text = "Vote",
                ReplySettings = XReplySettings.Following,
                Poll = new XPostPoll { Options = ["a", "b"], DurationMinutes = 60 },
            },
            CancellationToken.None);

        handler.Requests.Single().Body.Should().Be(
            """{"text":"Vote","poll":{"options":["a","b"],"duration_minutes":60},"reply_settings":"following"}""");
    }

    [Fact]
    public async Task Create_WithMediaTagsAndFlags_SerializesAllFields()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, Created);
        var client = TestClients.Create(handler);

        await client.Posts.CreateAsync(
            new CreatePostRequest
            {
                Text = "pic",
                Media = new XPostMedia { MediaIds = ["m1"], TaggedUserIds = ["u1"] },
                Geo = new XPostGeoRequest { PlaceId = "p1" },
                Nullcast = true,
                MadeWithAi = true,
            },
            CancellationToken.None);

        using var json = JsonDocument.Parse(handler.Requests.Single().Body!);
        var root = json.RootElement;
        root.GetProperty("media").GetProperty("media_ids")[0].GetString().Should().Be("m1");
        root.GetProperty("media").GetProperty("tagged_user_ids")[0].GetString().Should().Be("u1");
        root.GetProperty("geo").GetProperty("place_id").GetString().Should().Be("p1");
        root.GetProperty("nullcast").GetBoolean().Should().BeTrue();
        root.GetProperty("made_with_ai").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Edit_SendsEditOptions()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Created, Created);
        var client = TestClients.Create(handler);

        await client.Posts.EditAsync("99", new CreatePostRequest { Text = "fixed" }, CancellationToken.None);

        handler.Requests.Single().Body.Should().Be("""{"text":"fixed","edit_options":{"previous_post_id":"99"}}""");
    }

    public static TheoryData<CreatePostRequest> InvalidRequests => new()
    {
        new CreatePostRequest(),
        new CreatePostRequest { Text = "x", Poll = new XPostPoll { Options = ["only one"], DurationMinutes = 60 } },
        new CreatePostRequest { Text = "x", Poll = new XPostPoll { Options = ["a", "b"], DurationMinutes = 1 } },
        new CreatePostRequest { Text = "x", Poll = new XPostPoll { Options = ["a", new string('x', 26)], DurationMinutes = 60 } },
        new CreatePostRequest { Text = "x", Media = new XPostMedia { MediaIds = ["m"] }, Poll = new XPostPoll { Options = ["a", "b"], DurationMinutes = 60 } },
        new CreatePostRequest { Text = "x", Media = new XPostMedia { MediaIds = [] } },
        new CreatePostRequest { Text = "x", Media = new XPostMedia { MediaIds = ["m"], TaggedUserIds = [.. Enumerable.Range(0, 11).Select(i => i.ToString())] } },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_InvalidRequest_ThrowsBeforeSending(CreatePostRequest request)
    {
        var handler = new QueueHandler();
        var client = TestClients.Create(handler);

        var act = () => client.Posts.CreateAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateThread_ChainsEachPostToThePrevious()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.Created, """{"data":{"id":"1","text":"a"}}""")
            .Enqueue(HttpStatusCode.Created, """{"data":{"id":"2","text":"b"}}""")
            .Enqueue(HttpStatusCode.Created, """{"data":{"id":"3","text":"c"}}""");
        var client = TestClients.Create(handler);

        var result = await client.Posts.CreateThreadAsync(["a", "b", "c"], CancellationToken.None);

        result.IsComplete.Should().BeTrue();
        result.Posts.Select(p => p.Id).Should().Equal("1", "2", "3");
        handler.Requests[0].Body.Should().Be("""{"text":"a"}""");
        handler.Requests[1].Body.Should().Be("""{"text":"b","reply":{"in_reply_to_tweet_id":"1"}}""");
        handler.Requests[2].Body.Should().Be("""{"text":"c","reply":{"in_reply_to_tweet_id":"2"}}""");
    }

    [Fact]
    public async Task CreateThread_StopsAtFirstFailureAndNeverRetriesAPost()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.Created, """{"data":{"id":"1","text":"a"}}""")
            .Enqueue(HttpStatusCode.ServiceUnavailable, """{"title":"Service Unavailable"}""");
        var client = TestClients.Create(handler);

        var result = await client.Posts.CreateThreadAsync(["a", "b", "c"], CancellationToken.None);

        result.IsComplete.Should().BeFalse();
        result.FailedIndex.Should().Be(1);
        result.Posts.Should().ContainSingle().Which.Id.Should().Be("1");
        result.Error!.Kind.Should().Be(XApiErrorKind.Server);
        handler.Requests.Should().HaveCount(2, "a 503 on POST is ambiguous and must not be retried");
    }

    [Fact]
    public async Task Delete_UsesDeleteVerbAndRoute()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"deleted":true}}""");
        var client = TestClients.Create(handler);

        var response = await client.Posts.DeleteAsync("77", CancellationToken.None);

        response.Data!.Deleted.Should().BeTrue();
        handler.Requests.Single().Method.Should().Be(HttpMethod.Delete);
        handler.Requests.Single().PathAndQuery.Should().Be("/2/tweets/77");
    }

    [Fact]
    public async Task HideAndUnhide_SendPutWithHiddenFlag()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.OK, """{"data":{"hidden":true}}""")
            .Enqueue(HttpStatusCode.OK, """{"data":{"hidden":false}}""");
        var client = TestClients.Create(handler);

        var hidden = await client.Posts.HideReplyAsync("5", CancellationToken.None);
        var shown = await client.Posts.UnhideReplyAsync("5", CancellationToken.None);

        hidden.Data!.Hidden.Should().BeTrue();
        shown.Data!.Hidden.Should().BeFalse();
        handler.Requests[0].Method.Should().Be(HttpMethod.Put);
        handler.Requests[0].PathAndQuery.Should().Be("/2/tweets/5/hidden");
        handler.Requests[0].Body.Should().Be("""{"hidden":true}""");
        handler.Requests[1].Body.Should().Be("""{"hidden":false}""");
    }

    [Fact]
    public async Task Get_RequestsOnlyTheSelectedFields()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","text":"hi","public_metrics":{"like_count":3,"repost_count":1}}}""");
        var client = TestClients.Create(handler);

        var response = await client.Posts.GetAsync(
            "1",
            new XFieldSelection { PostFields = [XPostFields.PublicMetrics, XPostFields.CreatedAt], Expansions = [XPostExpansions.AuthorId] },
            CancellationToken.None);

        response.Data!.PublicMetrics!.LikeCount.Should().Be(3);
        handler.Requests.Single().PathAndQuery.Should().Be("/2/tweets/1?post.fields=public_metrics%2Ccreated_at&expansions=author_id");
    }

    [Fact]
    public async Task Get_WithoutFields_AddsNoQueryString()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":{"id":"1","text":"hi"}}""");
        var client = TestClients.Create(handler);

        await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        handler.Requests.Single().Uri.Query.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMany_RejectsMoreThan100Ids()
    {
        var client = TestClients.Create(new QueueHandler());

        var act = () => client.Posts.GetManyAsync([.. Enumerable.Range(1, 101).Select(i => i.ToString())], null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetMany_SurfacesPartialErrorsWithoutFailing()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK,
            """{"data":[{"id":"1","text":"a"}],"errors":[{"title":"Not Found Error","detail":"Could not find tweet with ids: [2].","type":"https://api.twitter.com/2/problems/resource-not-found","value":"2","resource_id":"2","resource_type":"tweet"}]}""");
        var client = TestClients.Create(handler);

        var response = await client.Posts.GetManyAsync(["1", "2"], null, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().ContainSingle();
        response.PartialErrors.Should().ContainSingle().Which.Detail.Should().Contain("Could not find tweet");
    }

    [Fact]
    public async Task SearchRecent_BuildsQueryWithEncodingAndPaging()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK,
            """{"data":[{"id":"1","text":"a"}],"meta":{"newest_id":"1","oldest_id":"1","result_count":1,"next_token":"abc"}}""");
        var client = TestClients.Create(handler);

        var response = await client.Posts.SearchRecentAsync(
            "from:xdev -is:retweet",
            new XSearchQuery
            {
                MaxResults = 50,
                PaginationToken = "tok",
                StartTime = new DateTimeOffset(2026, 3, 1, 10, 30, 0, TimeSpan.FromHours(2)),
                SortOrder = "recency",
            },
            CancellationToken.None);

        response.NextToken.Should().Be("abc");
        response.Meta!.ResultCount.Should().Be(1);
        handler.Requests.Single().PathAndQuery.Should().Be(
            "/2/tweets/search/recent?query=from%3Axdev%20-is%3Aretweet&max_results=50&pagination_token=tok&start_time=2026-03-01T08%3A30%3A00Z&sort_order=recency");
    }

    [Fact]
    public async Task GetCountsAll_AcceptsOnlyAppOnlyAuthentication()
    {
        var client = TestClients.Create(new QueueHandler());

        var act = () => client.Posts.GetCountsAllAsync("x", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AppOnly*");
    }

    [Fact]
    public async Task GetCountsRecent_ParsesBucketsAndTotals()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK,
            """{"data":[{"start":"2026-01-01T00:00:00.000Z","end":"2026-01-01T01:00:00.000Z","post_count":7}],"meta":{"total_post_count":7}}""");
        var client = TestClients.Create(handler, new XConnectOptions().UseBearerToken("bearer"));

        var response = await client.Posts.GetCountsRecentAsync("cats", new XCountsQuery { Granularity = "hour" }, CancellationToken.None);

        response.Data!.Single().PostCount.Should().Be(7);
        response.Meta!.TotalPostCount.Should().Be(7);
        handler.Requests.Single().Authorization.Should().Be("Bearer bearer");
    }

    [Fact]
    public async Task GetReplies_SearchesTheConversation()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, """{"data":[]}""");
        var client = TestClients.Create(handler);

        await client.Posts.GetRepliesAsync("42", null, CancellationToken.None);

        Uri.UnescapeDataString(handler.Requests.Single().Uri.Query).Should().Contain("query=conversation_id:42 is:reply");
    }

    [Fact]
    public async Task Response_ToleratesUnknownFieldsAndLargeIds()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK,
            """{"data":{"id":"1445880548472328192","text":"t","brand_new_field":{"nested":[1,2,3]},"public_metrics":{"like_count":1,"future_metric":9}},"meta":{"surprise":true}}""");
        var client = TestClients.Create(handler);

        var response = await client.Posts.GetAsync("1445880548472328192", cancellationToken: CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Id.Should().Be("1445880548472328192");
    }

    [Fact]
    public async Task Response_MalformedJson_BecomesSerializationError()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.OK, "{ this is not json");
        var client = TestClients.Create(handler);

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Error!.Kind.Should().Be(XApiErrorKind.Serialization);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDataOrThrow_OnFailure_ThrowsXApiException()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.NotFound, """{"title":"Not Found","detail":"gone","status":404}""");
        var client = TestClients.Create(handler);

        var response = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);
        var act = () => response.GetDataOrThrow();

        act.Should().Throw<XApiException>().Which.Error.Kind.Should().Be(XApiErrorKind.NotFound);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, XApiErrorKind.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, XApiErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, XApiErrorKind.Authorization)]
    [InlineData(HttpStatusCode.NotFound, XApiErrorKind.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity, XApiErrorKind.Validation)]
    [InlineData(HttpStatusCode.TooManyRequests, XApiErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, XApiErrorKind.Server)]
    public async Task Errors_AreClassifiedByStatus(HttpStatusCode status, XApiErrorKind expected)
    {
        var handler = new QueueHandler().Enqueue(status, """{"title":"t","detail":"d"}""");
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.Retry = new XRetryOptions { MaxRetries = 0 }));

        var response = await client.Posts.CreateAsync(new CreatePostRequest { Text = "x" }, CancellationToken.None);

        response.Error!.Kind.Should().Be(expected);
        response.Error.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task Errors_DuplicateContentIsRecognised()
    {
        var handler = new QueueHandler().Enqueue(HttpStatusCode.Forbidden,
            """{"title":"Forbidden","detail":"You are not allowed to create a Tweet with duplicate content.","type":"about:blank","status":403}""",
            r => r.Headers.Add("x-transaction-id", "abc123"));
        var client = TestClients.Create(handler);

        var response = await client.Posts.CreateAsync(new CreatePostRequest { Text = "x" }, CancellationToken.None);

        response.Error!.Kind.Should().Be(XApiErrorKind.DuplicatePost);
        response.Error.RequestId.Should().Be("abc123");
        response.RequestId.Should().Be("abc123");
    }

    [Fact]
    public async Task Errors_LegacyErrorsArrayAndPlainTextAreHandled()
    {
        var handler = new QueueHandler()
            .Enqueue(HttpStatusCode.Unauthorized, """{"errors":[{"code":32,"message":"Could not authenticate you."}]}""")
            .Enqueue(HttpStatusCode.BadGateway, "<html>Bad gateway</html>");
        var client = TestClients.Create(handler, TestClients.OAuth1Options().Also(o => o.Retry = new XRetryOptions { MaxRetries = 0 }));

        var first = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);
        var second = await client.Posts.GetAsync("1", cancellationToken: CancellationToken.None);

        first.Error!.Kind.Should().Be(XApiErrorKind.Authentication);
        first.Error.Detail.Should().Be("Could not authenticate you.");
        first.Error.ErrorCode.Should().Be(32);
        second.Error!.Kind.Should().Be(XApiErrorKind.Server);
        second.Error.Detail.Should().Contain("Bad gateway");
    }
}

internal static class OptionsExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
