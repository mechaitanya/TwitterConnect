using System.Net.Http.Json;
using XConnect.Authentication;
using XConnect.Infrastructure;
using XConnect.Models;
using XConnect.Serialization;

namespace XConnect.Clients;

/// <summary>Looking up users and their relationships. Request only the <see cref="XUserFields"/> you need.</summary>
public interface IXUsersClient
{
    /// <summary>Gets the authenticated user (<c>GET /2/users/me</c>). Requires user-context authentication.</summary>
    Task<XResponse<XUser>> GetMeAsync(XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets a user by ID.</summary>
    Task<XResponse<XUser>> GetByIdAsync(string userId, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets a user by @handle (without the at sign).</summary>
    Task<XResponse<XUser>> GetByUsernameAsync(string username, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets up to 100 users by ID.</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetManyAsync(IReadOnlyList<string> userIds, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets up to 100 users by @handle.</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetByUsernamesAsync(IReadOnlyList<string> usernames, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Searches users (<c>GET /2/users/search</c>).</summary>
    Task<XResponse<IReadOnlyList<XUser>>> SearchAsync(string query, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets a user's followers.</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetFollowersAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the accounts a user follows.</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetFollowingAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the authenticated user's followers without looking up their ID (<c>GET /2/users/me/followers</c>, user context).</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetMyFollowersAsync(XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the accounts the authenticated user follows (<c>GET /2/users/me/following</c>, user context).</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetMyFollowingAsync(XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the affiliates of an organisation account (<c>GET /2/users/{id}/affiliates</c>).</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetAffiliatesAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default);
}

internal sealed class XUsersClient(XApiTransport transport) : IXUsersClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<XUser>> GetMeAsync(XFieldSelection? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/users/me", XAuthMethods.UserContext).Add(fields), Json.EnvelopeXUser, cancellationToken);

    public Task<XResponse<XUser>> GetByIdAsync(string userId, XFieldSelection? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}", XAuthMethods.Any, ("id", userId)).Add(fields), Json.EnvelopeXUser, cancellationToken);

    public Task<XResponse<XUser>> GetByUsernameAsync(string username, XFieldSelection? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/users/by/username/{username}", XAuthMethods.Any, ("username", username)).Add(fields), Json.EnvelopeXUser, cancellationToken);

    public Task<XResponse<IReadOnlyList<XUser>>> GetManyAsync(IReadOnlyList<string> userIds, XFieldSelection? fields = null, CancellationToken cancellationToken = default)
    {
        RequireBatch(userIds);
        return transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/users", XAuthMethods.Any).Add("ids", userIds).Add(fields), Json.EnvelopeXUserList, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XUser>>> GetByUsernamesAsync(IReadOnlyList<string> usernames, XFieldSelection? fields = null, CancellationToken cancellationToken = default)
    {
        RequireBatch(usernames);
        return transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/users/by", XAuthMethods.Any).Add("usernames", usernames).Add(fields), Json.EnvelopeXUserList, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XUser>>> SearchAsync(string query, XPageQuery? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var request = XApiRequest.Create(HttpMethod.Get, "/2/users/search", XAuthMethods.Any).Add("query", query);
        if (options is not null)
        {
            // This endpoint paginates with next_token rather than pagination_token.
            request.Add(options.Fields).Add("max_results", options.MaxResults).Add("next_token", options.PaginationToken);
        }

        return transport.SendAsync(request, Json.EnvelopeXUserList, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XUser>>> GetFollowersAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        Page("/2/users/{id}/followers", userId, options, cancellationToken);

    public Task<XResponse<IReadOnlyList<XUser>>> GetFollowingAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        Page("/2/users/{id}/following", userId, options, cancellationToken);

    public Task<XResponse<IReadOnlyList<XUser>>> GetMyFollowersAsync(XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        Send(XApiRequest.Create(HttpMethod.Get, "/2/users/me/followers", XAuthMethods.UserContext), options, cancellationToken);

    public Task<XResponse<IReadOnlyList<XUser>>> GetMyFollowingAsync(XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        Send(XApiRequest.Create(HttpMethod.Get, "/2/users/me/following", XAuthMethods.UserContext), options, cancellationToken);

    public Task<XResponse<IReadOnlyList<XUser>>> GetAffiliatesAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        Page("/2/users/{id}/affiliates", userId, options, cancellationToken);

    private Task<XResponse<IReadOnlyList<XUser>>> Page(string route, string userId, XPageQuery? options, CancellationToken cancellationToken) =>
        Send(XApiRequest.Create(HttpMethod.Get, route, XAuthMethods.Any, ("id", userId)), options, cancellationToken);

    private Task<XResponse<IReadOnlyList<XUser>>> Send(XApiRequest request, XPageQuery? options, CancellationToken cancellationToken)
    {
        if (options is not null)
        {
            request.Add(options.Fields).Add("max_results", options.MaxResults).Add("pagination_token", options.PaginationToken);
        }

        return transport.SendAsync(request, Json.EnvelopeXUserList, cancellationToken);
    }

    private static void RequireBatch(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is 0 or > 100)
        {
            throw new ArgumentException("Provide between 1 and 100 values.", nameof(values));
        }
    }
}

/// <summary>Likes, reposts, bookmarks and relationship changes for the authenticated user. All calls require user-context authentication.</summary>
/// <remarks>
/// X requires the user ID in these routes to be the authenticated user. The client resolves it with
/// <c>GET /2/users/me</c> once and caches it; set <see cref="XConnectOptions.AuthenticatedUserId"/> to skip the lookup.
/// </remarks>
public interface IXEngagementClient
{
    /// <summary>Likes a Post (scope <c>like.write</c>).</summary>
    Task<XResponse<XLikeResult>> LikeAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>Removes a like.</summary>
    Task<XResponse<XLikeResult>> UnlikeAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>Reposts a Post (scope <c>tweet.write</c>).</summary>
    Task<XResponse<XRepostResult>> RepostAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>Undoes a repost.</summary>
    Task<XResponse<XRepostResult>> UndoRepostAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>Bookmarks a Post (scope <c>bookmark.write</c>).</summary>
    Task<XResponse<XBookmarkResult>> BookmarkAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>Removes a bookmark.</summary>
    Task<XResponse<XBookmarkResult>> RemoveBookmarkAsync(string postId, CancellationToken cancellationToken = default);

    /// <summary>Gets your bookmarks (scope <c>bookmark.read</c>).</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetBookmarksAsync(XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets your bookmark folders (scope <c>bookmark.read</c>).</summary>
    Task<XResponse<IReadOnlyList<XBookmarkFolder>>> GetBookmarkFoldersAsync(XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a bookmark folder (scope <c>bookmark.write</c>; OAuth 2.0 only).</summary>
    Task<XResponse<XBookmarkFolder>> CreateBookmarkFolderAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Gets the Posts in a bookmark folder. X returns only the Post IDs unless you request fields.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetBookmarksInFolderAsync(string folderId, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Follows a user (scope <c>follows.write</c>).</summary>
    Task<XResponse<XFollowResult>> FollowAsync(string targetUserId, CancellationToken cancellationToken = default);

    /// <summary>Unfollows a user.</summary>
    Task<XResponse<XUnfollowResult>> UnfollowAsync(string targetUserId, CancellationToken cancellationToken = default);

    /// <summary>Blocks a user (scope <c>block.write</c>).</summary>
    Task<XResponse<XBlockResult>> BlockAsync(string targetUserId, CancellationToken cancellationToken = default);

    /// <summary>Unblocks a user.</summary>
    Task<XResponse<XBlockResult>> UnblockAsync(string targetUserId, CancellationToken cancellationToken = default);

    /// <summary>Gets the accounts you block (scope <c>block.read</c>).</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetBlockedUsersAsync(XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Mutes a user (scope <c>mute.write</c>).</summary>
    Task<XResponse<XMuteResult>> MuteAsync(string targetUserId, CancellationToken cancellationToken = default);

    /// <summary>Unmutes a user.</summary>
    Task<XResponse<XMuteResult>> UnmuteAsync(string targetUserId, CancellationToken cancellationToken = default);

    /// <summary>Gets the accounts you mute (scope <c>mute.read</c>).</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetMutedUsersAsync(XPageQuery? options = null, CancellationToken cancellationToken = default);
}

internal sealed class XEngagementClient(XApiTransport transport, XUserIdResolver resolver) : IXEngagementClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public async Task<XResponse<XLikeResult>> LikeAsync(string postId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        var request = XApiRequest.Create(HttpMethod.Post, "/2/users/{id}/likes", XAuthMethods.UserContext, ("id", id))
            .WithContent(() => JsonContent.Create(new PostIdBody(postId), Json.PostIdBody));
        return await transport.SendAsync(request, Json.EnvelopeXLikeResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XLikeResult>> UnlikeAsync(string postId, CancellationToken cancellationToken = default)
    {
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/users/{id}/likes/{tweet_id}", XAuthMethods.UserContext, ("id", id), ("tweet_id", postId)),
            Json.EnvelopeXLikeResult,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XRepostResult>> RepostAsync(string postId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        var request = XApiRequest.Create(HttpMethod.Post, "/2/users/{id}/retweets", XAuthMethods.UserContext, ("id", id))
            .WithContent(() => JsonContent.Create(new PostIdBody(postId), Json.PostIdBody));
        return await transport.SendAsync(request, Json.EnvelopeXRepostResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XRepostResult>> UndoRepostAsync(string postId, CancellationToken cancellationToken = default)
    {
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/users/{id}/retweets/{source_tweet_id}", XAuthMethods.UserContext, ("id", id), ("source_tweet_id", postId)),
            Json.EnvelopeXRepostResult,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XBookmarkResult>> BookmarkAsync(string postId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postId);
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        var request = XApiRequest.Create(HttpMethod.Post, "/2/users/{id}/bookmarks", XAuthMethods.OAuth2User, ("id", id))
            .WithContent(() => JsonContent.Create(new PostIdBody(postId), Json.PostIdBody));
        return await transport.SendAsync(request, Json.EnvelopeXBookmarkResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XBookmarkResult>> RemoveBookmarkAsync(string postId, CancellationToken cancellationToken = default)
    {
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/users/{id}/bookmarks/{tweet_id}", XAuthMethods.OAuth2User, ("id", id), ("tweet_id", postId)),
            Json.EnvelopeXBookmarkResult,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<IReadOnlyList<XPost>>> GetBookmarksAsync(XPageQuery? options = null, CancellationToken cancellationToken = default)
    {
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            Page(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/bookmarks", XAuthMethods.UserContext, ("id", id)), options),
            Json.EnvelopeXPostList,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<IReadOnlyList<XBookmarkFolder>>> GetBookmarkFoldersAsync(XPageQuery? options = null, CancellationToken cancellationToken = default)
    {
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            Page(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/bookmarks/folders", XAuthMethods.UserContext, ("id", id)), options),
            Json.EnvelopeXBookmarkFolderList,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XBookmarkFolder>> CreateBookmarkFolderAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        var request = XApiRequest.Create(HttpMethod.Post, "/2/users/{id}/bookmarks/folders", XAuthMethods.OAuth2User, ("id", id))
            .WithContent(() => JsonContent.Create(new FolderBody(name), Json.FolderBody));
        return await transport.SendAsync(request, Json.EnvelopeXBookmarkFolder, cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<IReadOnlyList<XPost>>> GetBookmarksInFolderAsync(string folderId, XPageQuery? options = null, CancellationToken cancellationToken = default)
    {
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            Page(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/bookmarks/folders/{folder_id}", XAuthMethods.UserContext, ("id", id), ("folder_id", folderId)), options),
            Json.EnvelopeXPostList,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XFollowResult>> FollowAsync(string targetUserId, CancellationToken cancellationToken = default) =>
        await Target(HttpMethod.Post, "/2/users/{id}/following", targetUserId, Json.EnvelopeXFollowResult, cancellationToken).ConfigureAwait(false);

    public async Task<XResponse<XUnfollowResult>> UnfollowAsync(string targetUserId, CancellationToken cancellationToken = default) =>
        await Untarget("/2/users/{source_user_id}/following/{target_user_id}", targetUserId, Json.EnvelopeXUnfollowResult, cancellationToken).ConfigureAwait(false);

    public async Task<XResponse<XBlockResult>> BlockAsync(string targetUserId, CancellationToken cancellationToken = default) =>
        await Target(HttpMethod.Post, "/2/users/{id}/blocking", targetUserId, Json.EnvelopeXBlockResult, cancellationToken).ConfigureAwait(false);

    public async Task<XResponse<XBlockResult>> UnblockAsync(string targetUserId, CancellationToken cancellationToken = default) =>
        await Untarget("/2/users/{source_user_id}/blocking/{target_user_id}", targetUserId, Json.EnvelopeXBlockResult, cancellationToken).ConfigureAwait(false);

    public async Task<XResponse<IReadOnlyList<XUser>>> GetBlockedUsersAsync(XPageQuery? options = null, CancellationToken cancellationToken = default)
    {
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            Page(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/blocking", XAuthMethods.UserContext, ("id", id)), options),
            Json.EnvelopeXUserList,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XMuteResult>> MuteAsync(string targetUserId, CancellationToken cancellationToken = default) =>
        await Target(HttpMethod.Post, "/2/users/{id}/muting", targetUserId, Json.EnvelopeXMuteResult, cancellationToken).ConfigureAwait(false);

    public async Task<XResponse<XMuteResult>> UnmuteAsync(string targetUserId, CancellationToken cancellationToken = default) =>
        await Untarget("/2/users/{source_user_id}/muting/{target_user_id}", targetUserId, Json.EnvelopeXMuteResult, cancellationToken).ConfigureAwait(false);

    public async Task<XResponse<IReadOnlyList<XUser>>> GetMutedUsersAsync(XPageQuery? options = null, CancellationToken cancellationToken = default)
    {
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            Page(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/muting", XAuthMethods.UserContext, ("id", id)), options),
            Json.EnvelopeXUserList,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<XResponse<T>> Target<T>(
        HttpMethod method,
        string route,
        string targetUserId,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<XEnvelope<T>> typeInfo,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        var request = XApiRequest.Create(method, route, XAuthMethods.UserContext, ("id", id))
            .WithContent(() => JsonContent.Create(new TargetUserBody(targetUserId), Json.TargetUserBody));
        return await transport.SendAsync(request, typeInfo, cancellationToken).ConfigureAwait(false);
    }

    private async Task<XResponse<T>> Untarget<T>(
        string route,
        string targetUserId,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<XEnvelope<T>> typeInfo,
        CancellationToken cancellationToken)
    {
        var id = await GetUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, route, XAuthMethods.UserContext, ("source_user_id", id), ("target_user_id", targetUserId)),
            typeInfo,
            cancellationToken).ConfigureAwait(false);
    }

    private static XApiRequest Page(XApiRequest request, XPageQuery? options)
    {
        if (options is not null)
        {
            request.Add(options.Fields).Add("max_results", options.MaxResults).Add("pagination_token", options.PaginationToken);
        }

        return request;
    }

    private Task<string> GetUserIdAsync(CancellationToken cancellationToken) => resolver.GetAsync(cancellationToken);

    internal sealed record PostIdBody(string TweetId);

    internal sealed record FolderBody(string Name);

    internal sealed record TargetUserBody(string TargetUserId);
}
