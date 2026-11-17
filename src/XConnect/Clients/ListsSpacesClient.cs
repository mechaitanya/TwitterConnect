using System.Net.Http.Json;
using XConnect.Authentication;
using XConnect.Infrastructure;
using XConnect.Models;
using XConnect.Serialization;

namespace XConnect.Clients;

/// <summary>Managing and reading Lists.</summary>
public interface IXListsClient
{
    /// <summary>Creates a List (scope <c>list.write</c>).</summary>
    Task<XResponse<XList>> CreateAsync(XListRequest request, CancellationToken cancellationToken = default);

    /// <summary>Updates a List you own.</summary>
    Task<XResponse<XListUpdateResult>> UpdateAsync(string listId, XListRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a List you own.</summary>
    Task<XResponse<XListDeleteResult>> DeleteAsync(string listId, CancellationToken cancellationToken = default);

    /// <summary>Gets a List by ID. Use <see cref="XFieldSelection.ListFields"/> to choose fields.</summary>
    Task<XResponse<XList>> GetAsync(string listId, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets Lists owned by a user.</summary>
    Task<XResponse<IReadOnlyList<XList>>> GetOwnedAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets Lists a user follows.</summary>
    Task<XResponse<IReadOnlyList<XList>>> GetFollowedAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the members of a List.</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetMembersAsync(string listId, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the Posts of a List.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetPostsAsync(string listId, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Adds a member to a List you own.</summary>
    Task<XResponse<XListMemberResult>> AddMemberAsync(string listId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Removes a member from a List you own.</summary>
    Task<XResponse<XListMemberResult>> RemoveMemberAsync(string listId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Gets the followers of a List.</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetFollowersAsync(string listId, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the Lists a user is a member of (<c>GET /2/users/{id}/list_memberships</c>).</summary>
    Task<XResponse<IReadOnlyList<XList>>> GetMembershipsAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets your pinned Lists. Requires user-context authentication.</summary>
    Task<XResponse<IReadOnlyList<XList>>> GetPinnedAsync(XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Pins a List you own or follow.</summary>
    Task<XResponse<XPinResult>> PinAsync(string listId, CancellationToken cancellationToken = default);

    /// <summary>Unpins a List.</summary>
    Task<XResponse<XPinResult>> UnpinAsync(string listId, CancellationToken cancellationToken = default);

    /// <summary>Follows a List as the authenticated user.</summary>
    Task<XResponse<XListFollowResult>> FollowAsync(string listId, CancellationToken cancellationToken = default);

    /// <summary>Unfollows a List as the authenticated user.</summary>
    Task<XResponse<XListFollowResult>> UnfollowAsync(string listId, CancellationToken cancellationToken = default);
}

internal sealed class XListsClient(XApiTransport transport, XUserIdResolver resolver) : IXListsClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<XList>> CreateAsync(XListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        var message = XApiRequest.Create(HttpMethod.Post, "/2/lists", XAuthMethods.UserContext)
            .WithContent(() => JsonContent.Create(request, Json.XListRequest));
        return transport.SendAsync(message, Json.EnvelopeXList, cancellationToken);
    }

    public Task<XResponse<XListUpdateResult>> UpdateAsync(string listId, XListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var message = XApiRequest.Create(HttpMethod.Put, "/2/lists/{id}", XAuthMethods.UserContext, ("id", listId))
            .WithContent(() => JsonContent.Create(request, Json.XListRequest));
        return transport.SendAsync(message, Json.EnvelopeXListUpdateResult, cancellationToken);
    }

    public Task<XResponse<XListDeleteResult>> DeleteAsync(string listId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/lists/{id}", XAuthMethods.UserContext, ("id", listId)),
            Json.EnvelopeXListDeleteResult,
            cancellationToken);

    public Task<XResponse<XList>> GetAsync(string listId, XFieldSelection? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/lists/{id}", XAuthMethods.Any, ("id", listId)).Add(fields),
            Json.EnvelopeXList,
            cancellationToken);

    public Task<XResponse<IReadOnlyList<XList>>> GetOwnedAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(Page(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/owned_lists", XAuthMethods.Any, ("id", userId)), options), Json.EnvelopeXListList, cancellationToken);

    public Task<XResponse<IReadOnlyList<XList>>> GetFollowedAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(Page(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/followed_lists", XAuthMethods.Any, ("id", userId)), options), Json.EnvelopeXListList, cancellationToken);

    public Task<XResponse<IReadOnlyList<XUser>>> GetMembersAsync(string listId, XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(Page(XApiRequest.Create(HttpMethod.Get, "/2/lists/{id}/members", XAuthMethods.Any, ("id", listId)), options), Json.EnvelopeXUserList, cancellationToken);

    public Task<XResponse<IReadOnlyList<XPost>>> GetPostsAsync(string listId, XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(Page(XApiRequest.Create(HttpMethod.Get, "/2/lists/{id}/tweets", XAuthMethods.Any, ("id", listId)), options), Json.EnvelopeXPostList, cancellationToken);

    public Task<XResponse<XListMemberResult>> AddMemberAsync(string listId, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var message = XApiRequest.Create(HttpMethod.Post, "/2/lists/{id}/members", XAuthMethods.UserContext, ("id", listId))
            .WithContent(() => JsonContent.Create(new UserIdBody(userId), Json.UserIdBody));
        return transport.SendAsync(message, Json.EnvelopeXListMemberResult, cancellationToken);
    }

    public Task<XResponse<XListMemberResult>> RemoveMemberAsync(string listId, string userId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/lists/{id}/members/{user_id}", XAuthMethods.UserContext, ("id", listId), ("user_id", userId)),
            Json.EnvelopeXListMemberResult,
            cancellationToken);

    public Task<XResponse<IReadOnlyList<XUser>>> GetFollowersAsync(string listId, XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(Page(XApiRequest.Create(HttpMethod.Get, "/2/lists/{id}/followers", XAuthMethods.Any, ("id", listId)), options), Json.EnvelopeXUserList, cancellationToken);

    public Task<XResponse<IReadOnlyList<XList>>> GetMembershipsAsync(string userId, XPageQuery? options = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(Page(XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/list_memberships", XAuthMethods.Any, ("id", userId)), options), Json.EnvelopeXListList, cancellationToken);

    public async Task<XResponse<IReadOnlyList<XList>>> GetPinnedAsync(XFieldSelection? fields = null, CancellationToken cancellationToken = default)
    {
        var me = await resolver.GetAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/users/{id}/pinned_lists", XAuthMethods.UserContext, ("id", me)).Add(fields),
            Json.EnvelopeXListList,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XPinResult>> PinAsync(string listId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listId);
        var me = await resolver.GetAsync(cancellationToken).ConfigureAwait(false);
        var message = XApiRequest.Create(HttpMethod.Post, "/2/users/{id}/pinned_lists", XAuthMethods.UserContext, ("id", me))
            .WithContent(() => JsonContent.Create(new ListIdBody(listId), Json.ListIdBody));
        return await transport.SendAsync(message, Json.EnvelopeXPinResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XPinResult>> UnpinAsync(string listId, CancellationToken cancellationToken = default)
    {
        var me = await resolver.GetAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/users/{id}/pinned_lists/{list_id}", XAuthMethods.UserContext, ("id", me), ("list_id", listId)),
            Json.EnvelopeXPinResult,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XListFollowResult>> FollowAsync(string listId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listId);
        var me = await resolver.GetAsync(cancellationToken).ConfigureAwait(false);
        var message = XApiRequest.Create(HttpMethod.Post, "/2/users/{id}/followed_lists", XAuthMethods.UserContext, ("id", me))
            .WithContent(() => JsonContent.Create(new ListIdBody(listId), Json.ListIdBody));
        return await transport.SendAsync(message, Json.EnvelopeXListFollowResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task<XResponse<XListFollowResult>> UnfollowAsync(string listId, CancellationToken cancellationToken = default)
    {
        var me = await resolver.GetAsync(cancellationToken).ConfigureAwait(false);
        return await transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/users/{id}/followed_lists/{list_id}", XAuthMethods.UserContext, ("id", me), ("list_id", listId)),
            Json.EnvelopeXListFollowResult,
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

    internal sealed record UserIdBody(string UserId);

    internal sealed record ListIdBody(string ListId);
}

/// <summary>Looking up Spaces. Spaces endpoints accept an app-only Bearer token or OAuth 2.0 user context (not OAuth 1.0a).</summary>
public interface IXSpacesClient
{
    /// <summary>Gets a Space by ID. Use <see cref="XFieldSelection.SpaceFields"/> to choose fields.</summary>
    Task<XResponse<XSpace>> GetAsync(string spaceId, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets up to 100 Spaces by ID.</summary>
    Task<XResponse<IReadOnlyList<XSpace>>> GetManyAsync(IReadOnlyList<string> spaceIds, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets Spaces created by the given users.</summary>
    Task<XResponse<IReadOnlyList<XSpace>>> GetByCreatorsAsync(IReadOnlyList<string> userIds, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Searches Spaces. <paramref name="state"/> is <c>live</c>, <c>scheduled</c> or <c>all</c>.</summary>
    Task<XResponse<IReadOnlyList<XSpace>>> SearchAsync(string query, string? state = null, int? maxResults = null, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the Posts shared in a Space.</summary>
    Task<XResponse<IReadOnlyList<XPost>>> GetPostsAsync(string spaceId, XPageQuery? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets the users who bought tickets to a Space (<c>GET /2/spaces/{id}/buyers</c>). OAuth 2.0 user context only; X does not accept app-only or OAuth 1.0a here.</summary>
    Task<XResponse<IReadOnlyList<XUser>>> GetTicketBuyersAsync(string spaceId, XPageQuery? options = null, CancellationToken cancellationToken = default);
}

internal sealed class XSpacesClient(XApiTransport transport) : IXSpacesClient
{
    private const XAuthMethods Auth = XAuthMethods.OAuth2User | XAuthMethods.AppOnly;
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<XSpace>> GetAsync(string spaceId, XFieldSelection? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/spaces/{id}", Auth, ("id", spaceId)).Add(fields), Json.EnvelopeXSpace, cancellationToken);

    public Task<XResponse<IReadOnlyList<XSpace>>> GetManyAsync(IReadOnlyList<string> spaceIds, XFieldSelection? fields = null, CancellationToken cancellationToken = default)
    {
        Require(spaceIds);
        return transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/spaces", Auth).Add("ids", spaceIds).Add(fields), Json.EnvelopeXSpaceList, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XSpace>>> GetByCreatorsAsync(IReadOnlyList<string> userIds, XFieldSelection? fields = null, CancellationToken cancellationToken = default)
    {
        Require(userIds);
        return transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/spaces/by/creator_ids", Auth).Add("user_ids", userIds).Add(fields), Json.EnvelopeXSpaceList, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XSpace>>> SearchAsync(string query, string? state = null, int? maxResults = null, XFieldSelection? fields = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var request = XApiRequest.Create(HttpMethod.Get, "/2/spaces/search", Auth).Add("query", query).Add("state", state).Add("max_results", maxResults).Add(fields);
        return transport.SendAsync(request, Json.EnvelopeXSpaceList, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XPost>>> GetPostsAsync(string spaceId, XPageQuery? options = null, CancellationToken cancellationToken = default)
    {
        var request = XApiRequest.Create(HttpMethod.Get, "/2/spaces/{id}/tweets", Auth, ("id", spaceId));
        if (options is not null)
        {
            request.Add(options.Fields).Add("max_results", options.MaxResults).Add("pagination_token", options.PaginationToken);
        }

        return transport.SendAsync(request, Json.EnvelopeXPostList, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XUser>>> GetTicketBuyersAsync(string spaceId, XPageQuery? options = null, CancellationToken cancellationToken = default)
    {
        var request = XApiRequest.Create(HttpMethod.Get, "/2/spaces/{id}/buyers", XAuthMethods.OAuth2User, ("id", spaceId));
        if (options is not null)
        {
            request.Add(options.Fields).Add("max_results", options.MaxResults).Add("pagination_token", options.PaginationToken);
        }

        return transport.SendAsync(request, Json.EnvelopeXUserList, cancellationToken);
    }

    private static void Require(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count is 0 or > 100)
        {
            throw new ArgumentException("Provide between 1 and 100 values.", nameof(values));
        }
    }
}
