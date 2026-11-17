using System.Net.Http.Json;
using XConnect.Authentication;
using XConnect.Infrastructure;
using XConnect.Models;
using XConnect.Serialization;

namespace XConnect.Clients;

/// <summary>Creating, deleting, reading and searching Posts.</summary>
public interface IXPostsClient
{
    /// <summary>Creates a Post, reply, quote, poll or media Post (<c>POST /2/tweets</c>). User-context authentication only.</summary>
    /// <remarks>Post creation is never retried automatically after an ambiguous network failure.</remarks>
    /// <exception cref="ArgumentException">The request is invalid, for example no text and no media.</exception>
    Task<XResponse<XCreatedPost>> CreateAsync(CreatePostRequest request, CancellationToken cancellationToken = default);

    /// <summary>Replies to a Post.</summary>
    Task<XResponse<XCreatedPost>> ReplyAsync(string inReplyToPostId, string text, CancellationToken cancellationToken = default);

    /// <summary>Quotes a Post. X documents quote Posting as requiring an Enterprise plan.</summary>
    Task<XResponse<XCreatedPost>> QuoteAsync(string quotedPostId, string text, CancellationToken cancellationToken = default);

    /// <summary>Edits a Post that is still eligible for editing, through <c>edit_options</c> on <c>POST /2/tweets</c>.</summary>
    Task<XResponse<XCreatedPost>> EditAsync(string previousPostId, CreatePostRequest replacement, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a thread: the first Post is created, and each following Post replies to the previous one.
    /// Creation stops at the first failure and nothing is retried or rolled back, so inspect <see cref="XThreadResult"/>.
    /// </summary>
    Task<XThreadResult> CreateThreadAsync(IReadOnlyList<CreatePostRequest> posts, CancellationToken cancellationToken = default);

    /// <summary>Creates a thread from plain texts.</summary>
    Task<XThreadResult> CreateThreadAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);

    /// <summary>Deletes a Post (<c>DELETE /2/tweets/{id}</c>).</summary>
    Task<XResponse<XDeleteResult>> DeleteAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>Hides a reply to one of your Posts (<c>PUT /2/tweets/{id}/hidden</c>).</summary>
    Task<XResponse<XHiddenResult>> HideReplyAsync(string replyPostId, CancellationToken cancellationToken = default);

    /// <summary>Unhides a reply.</summary>
    Task<XResponse<XHiddenResult>> UnhideReplyAsync(string replyPostId, CancellationToken cancellationToken = default);

    /// <summary>Gets a Post by ID.</summary>
    Task<XResponse<XPost>> GetAsync(string postId, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets up to 100 Posts by ID.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetManyAsync(IReadOnlyList<string> postIds, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the Posts written by a user (<c>GET /2/users/{id}/tweets</c>).</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetUserPostsAsync(string userId, XPostQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets Posts that mention a user.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetMentionsAsync(string userId, XPostQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the reverse-chronological home timeline. Requires user-context authentication.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetHomeTimelineAsync(string userId, XPostQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets Posts liked by a user. Requires user-context authentication and the <c>like.read</c> scope.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetLikedPostsAsync(string userId, XPageQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets users who reposted a Post.</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetRepostingUsersAsync(string postId, XPageQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets users who liked a Post. Requires user-context authentication and the <c>like.read</c> scope.</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetLikingUsersAsync(string postId, XPageQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the reposts of a Post as Post objects (<c>GET /2/tweets/{id}/retweets</c>). To get the reposting users use <see cref="GetRepostingUsersAsync"/>.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetRepostsAsync(string postId, XPageQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets your Posts that others have reposted (<c>GET /2/users/reposts_of_me</c>). Requires user context and the <c>timeline.read</c> scope.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetRepostsOfMeAsync(XPageQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets Posts that quote a Post.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetQuotePostsAsync(string postId, XPostQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets replies to a Post by searching its conversation: <c>conversation_id:{id} is:reply</c>.
    /// X has no dedicated replies endpoint; this uses recent search and so inherits its seven-day window.
    /// </summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetRepliesAsync(string conversationId, XSearchQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Searches Posts from the last seven days.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> SearchRecentAsync(string query, XSearchQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Searches the full archive. Requires an access level that includes full-archive search.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> SearchAllAsync(string query, XSearchQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Counts matching Posts from the last seven days, bucketed by time. Accepts a Bearer token or OAuth 2.0 user context (not OAuth 1.0a).</summary>
    Task<XResponse<IReadOnlyList<XPostCount>>> GetCountsRecentAsync(string query, XCountsQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Counts matching Posts across the full archive. Requires an app-only Bearer token and the matching access level.</summary>
    Task<XResponse<IReadOnlyList<XPostCount>>> GetCountsAllAsync(string query, XCountsQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets analytics for your Posts (<c>GET /2/tweets/analytics</c>). Requires user-context authentication.
    /// </summary>
    /// <param name="postIds">The Post IDs.</param>
    /// <param name="startTime">Start of the window.</param>
    /// <param name="endTime">End of the window.</param>
    /// <param name="granularity">One of <c>hourly</c>, <c>daily</c>, <c>weekly</c> or <c>total</c>.</param>
    /// <param name="metrics">The <c>analytics.fields</c> to return, for example <c>impressions</c> and <c>likes</c>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<XResponse<IReadOnlyList<XPostAnalytics>>> GetAnalyticsAsync(
        IReadOnlyList<string> postIds,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        string? granularity = null,
        IReadOnlyList<string>? metrics = null,
        CancellationToken cancellationToken = default);
}

internal sealed class XPostsClient(XApiTransport transport) : IXPostsClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<XCreatedPost>> CreateAsync(CreatePostRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var message = XApiRequest.Create(HttpMethod.Post, "/2/tweets", XAuthMethods.UserContext)
            .WithContent(() => JsonContent.Create(request, Json.CreatePostRequest));
        return transport.SendAsync(message, Json.EnvelopeXCreatedPost, cancellationToken);
    }

    public Task<XResponse<XCreatedPost>> ReplyAsync(string inReplyToPostId, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inReplyToPostId);
        return CreateAsync(new CreatePostRequest { Text = text, Reply = new XPostReply { InReplyToTweetId = inReplyToPostId } }, cancellationToken);
    }

    public Task<XResponse<XCreatedPost>> QuoteAsync(string quotedPostId, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quotedPostId);
        return CreateAsync(new CreatePostRequest { Text = text, QuoteTweetId = quotedPostId }, cancellationToken);
    }

    public Task<XResponse<XCreatedPost>> EditAsync(string previousPostId, CreatePostRequest replacement, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousPostId);
        ArgumentNullException.ThrowIfNull(replacement);
        return CreateAsync(replacement with { EditOptions = new XPostEditOptions { PreviousPostId = previousPostId } }, cancellationToken);
    }

    public async Task<XThreadResult> CreateThreadAsync(IReadOnlyList<CreatePostRequest> posts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(posts);
        if (posts.Count == 0)
        {
            throw new ArgumentException("A thread needs at least one Post.", nameof(posts));
        }

        foreach (var post in posts)
        {
            Validate(post);
        }

        var created = new List<XCreatedPost>(posts.Count);
        for (var i = 0; i < posts.Count; i++)
        {
            var request = i == 0 ? posts[i] : posts[i] with { Reply = new XPostReply { InReplyToTweetId = created[^1].Id } };
            var response = await CreateAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.Data is null || !response.IsSuccess)
            {
                var error = response.Error ?? new XApiError(XApiErrorKind.Serialization, response.StatusCode, null, null, "X returned no Post.", null, response.RequestId);
                return new XThreadResult { Posts = created, Error = error, FailedIndex = i };
            }

            created.Add(response.Data);
        }

        return new XThreadResult { Posts = created };
    }

    public Task<XThreadResult> CreateThreadAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return CreateThreadAsync(texts.Select(t => new CreatePostRequest { Text = t }).ToArray(), cancellationToken);
    }

    public Task<XResponse<XDeleteResult>> DeleteAsync(string postId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/tweets/{id}", XAuthMethods.UserContext, ("id", postId)),
            Json.EnvelopeXDeleteResult,
            cancellationToken);

    public Task<XResponse<XHiddenResult>> HideReplyAsync(string replyPostId, CancellationToken cancellationToken = default) =>
        SetHiddenAsync(replyPostId, hidden: true, cancellationToken);

    public Task<XResponse<XHiddenResult>> UnhideReplyAsync(string replyPostId, CancellationToken cancellationToken = default) =>
        SetHiddenAsync(replyPostId, hidden: false, cancellationToken);

    public Task<XResponse<XPost>> GetAsync(string postId, XFieldSelection? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/tweets/{id}", XAuthMethods.Any, ("id", postId)).Add(fields),
            Json.EnvelopeXPost,
            cancellationToken);

    public Task<XResponse<IReadOnlyList<XPost>>> GetManyAsync(IReadOnlyList<string> postIds, XFieldSelection? fields = null, CancellationToken cancellationToken = default)
    {
        RequireIds(postIds, 100);
        return transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/tweets", XAuthMethods.Any).Add("ids", postIds).Add(fields),
            Json.EnvelopeXPostList,
            cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XPost>>> GetUserPostsAsync(string userId, XPostQuery? query = null, CancellationToken cancellationToken = default) =>
        GetPostPage(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/tweets", XAuthMethods.Any, ("id", userId)), query, withExclude: true, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPost>>> GetMentionsAsync(string userId, XPostQuery? query = null, CancellationToken cancellationToken = default) =>
        GetPostPage(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/mentions", XAuthMethods.Any, ("id", userId)), query, withExclude: false, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPost>>> GetHomeTimelineAsync(string userId, XPostQuery? query = null, CancellationToken cancellationToken = default) =>
        GetPostPage(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/timelines/reverse_chronological", XAuthMethods.UserContext, ("id", userId)), query, withExclude: true, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPost>>> GetLikedPostsAsync(string userId, XPageQuery? query = null, CancellationToken cancellationToken = default) =>
        GetPage(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/liked_tweets", XAuthMethods.UserContext, ("id", userId)), query, Json.EnvelopeXPostList, cancellationToken);

    public Task<XResponse<IReadOnlyList<XUser>>> GetRepostingUsersAsync(string postId, XPageQuery? query = null, CancellationToken cancellationToken = default) =>
        GetPage(XApiRequest.Create(HttpMethod.Get, "/2/tweets/{id}/retweeted_by", XAuthMethods.Any, ("id", postId)), query, Json.EnvelopeXUserList, cancellationToken);

    public Task<XResponse<IReadOnlyList<XUser>>> GetLikingUsersAsync(string postId, XPageQuery? query = null, CancellationToken cancellationToken = default) =>
        GetPage(XApiRequest.Create(HttpMethod.Get, "/2/tweets/{id}/liking_users", XAuthMethods.UserContext, ("id", postId)), query, Json.EnvelopeXUserList, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPost>>> GetRepostsAsync(string postId, XPageQuery? query = null, CancellationToken cancellationToken = default) =>
        GetPage(XApiRequest.Create(HttpMethod.Get, "/2/tweets/{id}/retweets", XAuthMethods.Any, ("id", postId)), query, Json.EnvelopeXPostList, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPost>>> GetRepostsOfMeAsync(XPageQuery? query = null, CancellationToken cancellationToken = default) =>
        GetPage(XApiRequest.Create(HttpMethod.Get, "/2/users/reposts_of_me", XAuthMethods.UserContext), query, Json.EnvelopeXPostList, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPost>>> GetQuotePostsAsync(string postId, XPostQuery? query = null, CancellationToken cancellationToken = default) =>
        GetPostPage(XApiRequest.Create(HttpMethod.Get, "/2/tweets/{id}/quote_tweets", XAuthMethods.Any, ("id", postId)), query, withExclude: true, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPost>>> GetRepliesAsync(string conversationId, XSearchQuery? query = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        return SearchRecentAsync($"conversation_id:{conversationId} is:reply", query, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XPost>>> SearchRecentAsync(string query, XSearchQuery? options = null, CancellationToken cancellationToken = default) =>
        Search("/2/tweets/search/recent", query, options, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPost>>> SearchAllAsync(string query, XSearchQuery? options = null, CancellationToken cancellationToken = default) =>
        Search("/2/tweets/search/all", query, options, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPostCount>>> GetCountsRecentAsync(string query, XCountsQuery? options = null, CancellationToken cancellationToken = default) =>
        Counts("/2/tweets/counts/recent", XAuthMethods.OAuth2User | XAuthMethods.AppOnly, query, options, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPostCount>>> GetCountsAllAsync(string query, XCountsQuery? options = null, CancellationToken cancellationToken = default) =>
        Counts("/2/tweets/counts/all", XAuthMethods.AppOnly, query, options, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPostAnalytics>>> GetAnalyticsAsync(
        IReadOnlyList<string> postIds,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        string? granularity = null,
        IReadOnlyList<string>? metrics = null,
        CancellationToken cancellationToken = default)
    {
        RequireIds(postIds, 100);
        var request = XApiRequest.Create(HttpMethod.Get, "/2/tweets/analytics", XAuthMethods.UserContext)
            .Add("ids", postIds)
            .Add("start_time", startTime)
            .Add("end_time", endTime)
            .Add("granularity", granularity)
            .Add("analytics.fields", metrics);
        return transport.SendAsync(request, Json.EnvelopeXPostAnalyticsList, cancellationToken);
    }

    private Task<XResponse<XHiddenResult>> SetHiddenAsync(string replyPostId, bool hidden, CancellationToken cancellationToken)
    {
        var message = XApiRequest.Create(HttpMethod.Put, "/2/tweets/{tweet_id}/hidden", XAuthMethods.UserContext, ("tweet_id", replyPostId))
            .WithContent(() => JsonContent.Create(new HiddenBody(hidden), Json.HiddenBody));
        return transport.SendAsync(message, Json.EnvelopeXHiddenResult, cancellationToken);
    }

    private Task<XResponse<IReadOnlyList<XPost>>> GetPostPage(XApiRequest request, XPostQuery? query, bool withExclude, CancellationToken cancellationToken)
    {
        if (query is not null)
        {
            request.Add(query.Fields)
                .Add("max_results", query.MaxResults)
                .Add("pagination_token", query.PaginationToken)
                .Add("start_time", query.StartTime)
                .Add("end_time", query.EndTime)
                .Add("since_id", query.SinceId)
                .Add("until_id", query.UntilId);
            if (withExclude)
            {
                request.Add("exclude", query.Exclude);
            }
        }

        return transport.SendAsync(request, Json.EnvelopeXPostList, cancellationToken);
    }

    private Task<XResponse<IReadOnlyList<T>>> GetPage<T>(
        XApiRequest request,
        XPageQuery? query,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<XEnvelope<IReadOnlyList<T>>> typeInfo,
        CancellationToken cancellationToken)
    {
        if (query is not null)
        {
            request.Add(query.Fields).Add("max_results", query.MaxResults).Add("pagination_token", query.PaginationToken);
        }

        return transport.SendAsync(request, typeInfo, cancellationToken);
    }

    private Task<XResponse<IReadOnlyList<XPost>>> Search(string route, string query, XSearchQuery? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var request = XApiRequest.Create(HttpMethod.Get, route, XAuthMethods.Any).Add("query", query);
        if (options is not null)
        {
            request.Add(options.Fields)
                .Add("max_results", options.MaxResults)
                .Add("pagination_token", options.PaginationToken)
                .Add("start_time", options.StartTime)
                .Add("end_time", options.EndTime)
                .Add("since_id", options.SinceId)
                .Add("until_id", options.UntilId)
                .Add("sort_order", options.SortOrder);
        }

        return transport.SendAsync(request, Json.EnvelopeXPostList, cancellationToken);
    }

    private Task<XResponse<IReadOnlyList<XPostCount>>> Counts(string route, XAuthMethods auth, string query, XCountsQuery? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var request = XApiRequest.Create(HttpMethod.Get, route, auth).Add("query", query);
        if (options is not null)
        {
            request.Add("granularity", options.Granularity)
                .Add("start_time", options.StartTime)
                .Add("end_time", options.EndTime)
                .Add("since_id", options.SinceId)
                .Add("until_id", options.UntilId)
                .Add("pagination_token", options.PaginationToken);
        }

        return transport.SendAsync(request, Json.EnvelopeXPostCountList, cancellationToken);
    }

    private static void RequireIds(IReadOnlyList<string> ids, int max)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count is 0 || ids.Count > max)
        {
            throw new ArgumentException($"Provide between 1 and {max} IDs.", nameof(ids));
        }
    }

    internal static void Validate(CreatePostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Text) && request.Media is null)
        {
            throw new ArgumentException("A Post needs text or media (X documents text as required unless media is provided).", nameof(request));
        }

        if (request.Media is { } media)
        {
            if (media.MediaIds is null || media.MediaIds.Count is 0 or > 20)
            {
                throw new ArgumentException("Media.MediaIds must contain between 1 and 20 IDs.", nameof(request));
            }

            if (media.TaggedUserIds is { Count: > 10 })
            {
                throw new ArgumentException("At most 10 users can be tagged in media.", nameof(request));
            }

            if (media.CallToActions is { } cta)
            {
                var variants = (cta.AppInstall is null ? 0 : 1) + (cta.VisitSite is null ? 0 : 1) + (cta.WatchNow is null ? 0 : 1);
                if (variants != 1)
                {
                    throw new ArgumentException("Set exactly one call-to-action variant.", nameof(request));
                }

                if (!IsHttps(cta.VisitSite?.Url ?? cta.WatchNow?.Url ?? "https://ok"))
                {
                    throw new ArgumentException("Call-to-action URLs must be HTTPS.", nameof(request));
                }
            }
        }

        if (request.Poll is { } poll)
        {
            if (request.Media is not null)
            {
                throw new ArgumentException("A Post cannot have both media and a poll.", nameof(request));
            }

            if (poll.Options is null || poll.Options.Count is < 2 or > 4 || poll.Options.Any(o => string.IsNullOrEmpty(o) || o.Length > 25))
            {
                throw new ArgumentException("A poll needs 2 to 4 options of 1 to 25 characters.", nameof(request));
            }

            if (poll.DurationMinutes is < 5 or > 10080)
            {
                throw new ArgumentException("Poll duration must be between 5 and 10080 minutes.", nameof(request));
            }
        }
    }

    private static bool IsHttps(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    internal sealed record HiddenBody(bool Hidden);
}
