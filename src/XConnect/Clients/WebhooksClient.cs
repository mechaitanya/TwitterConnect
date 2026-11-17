using System.Net.Http.Json;
using XConnect.Authentication;
using XConnect.Infrastructure;
using XConnect.Models;
using XConnect.Serialization;

namespace XConnect;

/// <summary>
/// Manages webhook registrations and Account Activity subscriptions. This client only calls X's management endpoints; receiving events needs your own
/// public HTTPS endpoint, for which <see cref="XWebhookSecurity"/> answers CRC challenges and verifies signatures.
/// </summary>
/// <remarks>Whether your plan includes these endpoints is decided by X; a <c>403</c> surfaces as <see cref="XApiErrorKind.Authorization"/>.</remarks>
public interface IXWebhooksClient
{
    /// <summary>Lists the application's webhooks (<c>GET /2/webhooks</c>).</summary>
    Task<XResponse<IReadOnlyList<XWebhookConfig>>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Registers a webhook (<c>POST /2/webhooks</c>). X immediately sends a CRC check to <paramref name="url"/>, which must be public HTTPS without a port.</summary>
    Task<XResponse<XWebhookConfig>> CreateAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>Triggers a CRC re-validation (<c>PUT /2/webhooks/{id}</c>).</summary>
    Task<XResponse<XWebhookValidation>> ValidateAsync(string webhookId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a webhook (<c>DELETE /2/webhooks/{id}</c>).</summary>
    Task<XResponse<XWebhookDeleteResult>> DeleteAsync(string webhookId, CancellationToken cancellationToken = default);

    /// <summary>Asks X to re-deliver events from up to the past 24 hours (<c>POST /2/webhooks/replay</c>, app-only). Both dates are UTC.</summary>
    Task<XResponse<XWebhookReplayJob>> CreateReplayJobAsync(string webhookId, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken = default);

    /// <summary>Lists webhooks linked to the filtered stream (<c>GET /2/tweets/search/webhooks</c>, app-only).</summary>
    Task<XResponse<IReadOnlyList<XWebhookStreamLink>>> GetStreamLinksAsync(CancellationToken cancellationToken = default);

    /// <summary>Links a webhook to the filtered stream (<c>POST /2/tweets/search/webhooks/{id}</c>, app-only).</summary>
    Task<XResponse<XWebhookLinkResult>> CreateStreamLinkAsync(string webhookId, CancellationToken cancellationToken = default);

    /// <summary>Removes the link (<c>DELETE /2/tweets/search/webhooks/{id}</c>, app-only).</summary>
    Task<XResponse<XWebhookDeleteResult>> DeleteStreamLinkAsync(string webhookId, CancellationToken cancellationToken = default);

    /// <summary>Subscribes the authenticated user to a webhook's Account Activity events (user context).</summary>
    Task<XResponse<XAccountActivitySubscription>> CreateSubscriptionAsync(string webhookId, CancellationToken cancellationToken = default);

    /// <summary>Checks whether the authenticated user is subscribed (user context).</summary>
    Task<XResponse<XAccountActivitySubscription>> ValidateSubscriptionAsync(string webhookId, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions of a webhook (app-only).</summary>
    Task<XResponse<XAccountActivitySubscriptionList>> GetSubscriptionsAsync(string webhookId, CancellationToken cancellationToken = default);

    /// <summary>Removes a user's subscription (app-only).</summary>
    Task<XResponse<XAccountActivitySubscription>> DeleteSubscriptionAsync(string webhookId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Gets the application's subscription counts (app-only).</summary>
    Task<XResponse<XAccountActivityCount>> GetSubscriptionCountAsync(CancellationToken cancellationToken = default);
}

internal sealed class XWebhooksClient(XApiTransport transport) : IXWebhooksClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<IReadOnlyList<XWebhookConfig>>> GetAsync(CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/webhooks", XAuthMethods.Any), Json.EnvelopeXWebhookConfigList, cancellationToken);

    public Task<XResponse<XWebhookConfig>> CreateAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("A webhook URL must be an absolute https URL.", nameof(url));
        }

        if (!uri.IsDefaultPort)
        {
            throw new ArgumentException("X does not accept a port in a webhook URL.", nameof(url));
        }

        var request = XApiRequest.Create(HttpMethod.Post, "/2/webhooks", XAuthMethods.Any)
            .WithContent(() => JsonContent.Create(new CreateBody(url), Json.CreateBody));
        return transport.SendAsync(request, Json.EnvelopeXWebhookConfig, cancellationToken);
    }

    public Task<XResponse<XWebhookValidation>> ValidateAsync(string webhookId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Put, "/2/webhooks/{webhook_id}", XAuthMethods.Any, ("webhook_id", Id(webhookId))), Json.EnvelopeXWebhookValidation, cancellationToken);

    public Task<XResponse<XWebhookDeleteResult>> DeleteAsync(string webhookId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Delete, "/2/webhooks/{webhook_id}", XAuthMethods.Any, ("webhook_id", Id(webhookId))), Json.EnvelopeXWebhookDeleteResult, cancellationToken);

    public Task<XResponse<XWebhookReplayJob>> CreateReplayJobAsync(string webhookId, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken = default)
    {
        Id(webhookId);
        if (Window(windowEnd) == Window(windowStart) || windowEnd <= windowStart)
        {
            throw new ArgumentException("The end of the replay window must be after its start.", nameof(windowEnd));
        }

        // The spec defines both dates as UTC strings in yyyymmddhhmm format (pattern ^[0-9]{12}$).
        var request = XApiRequest.Create(HttpMethod.Post, "/2/webhooks/replay", XAuthMethods.AppOnly)
            .WithContent(() => JsonContent.Create(new ReplayBody(webhookId, Window(windowStart), Window(windowEnd)), Json.ReplayBody));
        return transport.SendAsync(request, Json.EnvelopeXWebhookReplayJob, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XWebhookStreamLink>>> GetStreamLinksAsync(CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/tweets/search/webhooks", XAuthMethods.AppOnly), Json.EnvelopeXWebhookStreamLinkList, cancellationToken);

    public Task<XResponse<XWebhookLinkResult>> CreateStreamLinkAsync(string webhookId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Post, "/2/tweets/search/webhooks/{webhook_id}", XAuthMethods.AppOnly, ("webhook_id", Id(webhookId))), Json.EnvelopeXWebhookLinkResult, cancellationToken);

    public Task<XResponse<XWebhookDeleteResult>> DeleteStreamLinkAsync(string webhookId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Delete, "/2/tweets/search/webhooks/{webhook_id}", XAuthMethods.AppOnly, ("webhook_id", Id(webhookId))), Json.EnvelopeXWebhookDeleteResult, cancellationToken);

    public Task<XResponse<XAccountActivitySubscription>> CreateSubscriptionAsync(string webhookId, CancellationToken cancellationToken = default)
    {
        // The request schema is an empty object; send it explicitly so the server sees a JSON body.
        var request = XApiRequest.Create(HttpMethod.Post, "/2/account_activity/webhooks/{webhook_id}/subscriptions/all", XAuthMethods.UserContext, ("webhook_id", Id(webhookId)))
            .WithContent(() => JsonContent.Create(new EmptyBody(), Json.EmptyBody));
        return transport.SendAsync(request, Json.EnvelopeXAccountActivitySubscription, cancellationToken);
    }

    public Task<XResponse<XAccountActivitySubscription>> ValidateSubscriptionAsync(string webhookId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/account_activity/webhooks/{webhook_id}/subscriptions/all", XAuthMethods.UserContext, ("webhook_id", Id(webhookId))),
            Json.EnvelopeXAccountActivitySubscription,
            cancellationToken);

    public Task<XResponse<XAccountActivitySubscriptionList>> GetSubscriptionsAsync(string webhookId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/account_activity/webhooks/{webhook_id}/subscriptions/all/list", XAuthMethods.AppOnly, ("webhook_id", Id(webhookId))),
            Json.EnvelopeXAccountActivitySubscriptionList,
            cancellationToken);

    public Task<XResponse<XAccountActivitySubscription>> DeleteSubscriptionAsync(string webhookId, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/account_activity/webhooks/{webhook_id}/subscriptions/{user_id}/all", XAuthMethods.AppOnly, ("webhook_id", Id(webhookId)), ("user_id", userId)),
            Json.EnvelopeXAccountActivitySubscription,
            cancellationToken);
    }

    public Task<XResponse<XAccountActivityCount>> GetSubscriptionCountAsync(CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Get, "/2/account_activity/subscriptions/count", XAuthMethods.AppOnly), Json.EnvelopeXAccountActivityCount, cancellationToken);

    private static string Id(string webhookId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookId);
        return webhookId;
    }

    private static string Window(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);

    internal sealed record CreateBody(string Url);

    internal sealed record ReplayBody(string WebhookId, string FromDate, string ToDate);

    internal sealed record EmptyBody;
}
