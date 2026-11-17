using System.Net.Http.Json;
using XConnect.Authentication;
using XConnect.Infrastructure;
using XConnect.Models;
using XConnect.Serialization;

namespace XConnect.Clients;

/// <summary>A Direct Message to send.</summary>
public sealed record XDirectMessage
{
    /// <summary>The message text. At least one of <see cref="Text"/> and <see cref="MediaIds"/> is required.</summary>
    public string? Text { get; init; }

    /// <summary>IDs of media uploaded with a <c>dm_*</c> category (see <see cref="XMediaCategory"/>).</summary>
    public IReadOnlyList<string>? MediaIds { get; init; }
}

/// <summary>Options for reading Direct Message events.</summary>
public sealed record XDmQuery
{
    /// <summary>Page size, 1 to 100.</summary>
    public int? MaxResults { get; init; }

    /// <summary>Token from the previous page.</summary>
    public string? PaginationToken { get; init; }

    /// <summary>Any of <c>MessageCreate</c>, <c>ParticipantsJoin</c>, <c>ParticipantsLeave</c>.</summary>
    public IReadOnlyList<string>? EventTypes { get; init; }

    /// <summary>Fields and expansions. Use <see cref="XFieldSelection.DmEventFields"/> to pick event fields.</summary>
    public XFieldSelection? Fields { get; init; }
}

/// <summary>
/// Direct Messages (the unencrypted X API v2 DM endpoints). These need the <c>dm.read</c> / <c>dm.write</c> scopes with
/// user-context authentication, and your app's access level must include DMs.
/// </summary>
/// <remarks>Encrypted X Chat conversations (<c>/2/chat/...</c>) are a separate product and are not supported by this client.</remarks>
public interface IXDirectMessageClient
{
    /// <summary>Sends a message to one user, creating the one-to-one conversation if needed.</summary>
    Task<XResponse<XDmSendResult>> SendToParticipantAsync(string participantId, XDirectMessage message, CancellationToken cancellationToken = default);

    /// <summary>Sends a message to an existing conversation.</summary>
    Task<XResponse<XDmSendResult>> SendToConversationAsync(string conversationId, XDirectMessage message, CancellationToken cancellationToken = default);

    /// <summary>Creates a group conversation with a first message.</summary>
    Task<XResponse<XDmSendResult>> CreateGroupConversationAsync(IReadOnlyList<string> participantIds, XDirectMessage message, CancellationToken cancellationToken = default);

    /// <summary>Gets recent events across all of your conversations. X exposes conversations through their events.</summary>
    Task<XResponse<IReadOnlyList<XDmEvent>>> GetEventsAsync(XDmQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets events in one conversation.</summary>
    Task<XResponse<IReadOnlyList<XDmEvent>>> GetConversationEventsAsync(string conversationId, XDmQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets events of the one-to-one conversation with a user.</summary>
    Task<XResponse<IReadOnlyList<XDmEvent>>> GetEventsWithParticipantAsync(string participantId, XDmQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Gets one event by ID.</summary>
    Task<XResponse<XDmEvent>> GetEventAsync(string eventId, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Blocks Direct Messages from a user (<c>POST /2/users/{id}/dm/block</c>; scope <c>dm.write</c>).</summary>
    Task<XResponse<XDmBlockResult>> BlockSenderAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Unblocks Direct Messages from a user.</summary>
    Task<XResponse<XDmBlockResult>> UnblockSenderAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Deletes one of your events (scope <c>dm.write</c>).</summary>
    Task<XResponse<XDmDeleteResult>> DeleteEventAsync(string eventId, CancellationToken cancellationToken = default);
}

internal sealed class XDirectMessageClient(XApiTransport transport) : IXDirectMessageClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<XDmSendResult>> SendToParticipantAsync(string participantId, XDirectMessage message, CancellationToken cancellationToken = default) =>
        Send(XApiRequest.Create(HttpMethod.Post, "/2/dm_conversations/with/{participant_id}/messages", XAuthMethods.UserContext, ("participant_id", participantId)), ToBody(message), cancellationToken);

    public Task<XResponse<XDmSendResult>> SendToConversationAsync(string conversationId, XDirectMessage message, CancellationToken cancellationToken = default) =>
        Send(XApiRequest.Create(HttpMethod.Post, "/2/dm_conversations/{dm_conversation_id}/messages", XAuthMethods.UserContext, ("dm_conversation_id", conversationId)), ToBody(message), cancellationToken);

    public Task<XResponse<XDmSendResult>> CreateGroupConversationAsync(IReadOnlyList<string> participantIds, XDirectMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(participantIds);
        if (participantIds.Count == 0)
        {
            throw new ArgumentException("A group conversation needs at least one participant.", nameof(participantIds));
        }

        var body = new GroupBody("Group", participantIds, ToBody(message));
        var request = XApiRequest.Create(HttpMethod.Post, "/2/dm_conversations", XAuthMethods.UserContext)
            .WithContent(() => JsonContent.Create(body, Json.GroupBody));
        return transport.SendAsync(request, Json.EnvelopeXDmSendResult, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XDmEvent>>> GetEventsAsync(XDmQuery? query = null, CancellationToken cancellationToken = default) =>
        Events(XApiRequest.Create(HttpMethod.Get, "/2/dm_events", XAuthMethods.UserContext), query, cancellationToken);

    public Task<XResponse<IReadOnlyList<XDmEvent>>> GetConversationEventsAsync(string conversationId, XDmQuery? query = null, CancellationToken cancellationToken = default) =>
        Events(XApiRequest.Create(HttpMethod.Get, "/2/dm_conversations/{id}/dm_events", XAuthMethods.UserContext, ("id", conversationId)), query, cancellationToken);

    public Task<XResponse<IReadOnlyList<XDmEvent>>> GetEventsWithParticipantAsync(string participantId, XDmQuery? query = null, CancellationToken cancellationToken = default) =>
        Events(XApiRequest.Create(HttpMethod.Get, "/2/dm_conversations/with/{participant_id}/dm_events", XAuthMethods.UserContext, ("participant_id", participantId)), query, cancellationToken);

    public Task<XResponse<XDmEvent>> GetEventAsync(string eventId, XFieldSelection? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/dm_events/{event_id}", XAuthMethods.UserContext, ("event_id", eventId)).Add(fields),
            Json.EnvelopeXDmEvent,
            cancellationToken);

    public Task<XResponse<XDmBlockResult>> BlockSenderAsync(string userId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Post, "/2/users/{id}/dm/block", XAuthMethods.UserContext, ("id", userId)), Json.EnvelopeXDmBlockResult, cancellationToken);

    public Task<XResponse<XDmBlockResult>> UnblockSenderAsync(string userId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Post, "/2/users/{id}/dm/unblock", XAuthMethods.UserContext, ("id", userId)), Json.EnvelopeXDmBlockResult, cancellationToken);

    public Task<XResponse<XDmDeleteResult>> DeleteEventAsync(string eventId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/dm_events/{event_id}", XAuthMethods.UserContext, ("event_id", eventId)),
            Json.EnvelopeXDmDeleteResult,
            cancellationToken);

    private Task<XResponse<XDmSendResult>> Send(XApiRequest request, DmBody body, CancellationToken cancellationToken)
    {
        request.WithContent(() => JsonContent.Create(body, Json.DmBody));
        return transport.SendAsync(request, Json.EnvelopeXDmSendResult, cancellationToken);
    }

    private Task<XResponse<IReadOnlyList<XDmEvent>>> Events(XApiRequest request, XDmQuery? query, CancellationToken cancellationToken)
    {
        if (query is not null)
        {
            request.Add("max_results", query.MaxResults)
                .Add("pagination_token", query.PaginationToken)
                .Add("event_types", query.EventTypes)
                .Add(query.Fields);
        }

        return transport.SendAsync(request, Json.EnvelopeXDmEventList, cancellationToken);
    }

    private static DmBody ToBody(XDirectMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrEmpty(message.Text) && message.MediaIds is not { Count: > 0 })
        {
            throw new ArgumentException("A Direct Message needs text or media.", nameof(message));
        }

        return new DmBody(
            string.IsNullOrEmpty(message.Text) ? null : message.Text,
            message.MediaIds is { Count: > 0 } ids ? ids.Select(id => new DmAttachment(id)).ToArray() : null);
    }

    internal sealed record DmBody(string? Text, IReadOnlyList<DmAttachment>? Attachments);

    internal sealed record DmAttachment(string MediaId);

    internal sealed record GroupBody(string ConversationType, IReadOnlyList<string> ParticipantIds, DmBody Message);
}
