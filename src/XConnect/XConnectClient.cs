using Microsoft.Extensions.Logging;
using XConnect.Authentication;
using XConnect.Clients;
using XConnect.Infrastructure;
using XConnect.Streaming;

namespace XConnect;

/// <summary>
/// The entry point to the X API v2 SDK. It groups the API areas as properties that share one HTTP pipeline.
/// </summary>
/// <remarks>
/// <see cref="IXClient"/> and <see cref="XClient"/> from XConnect 2.0 remain available for the single-call
/// <c>CreatePostAsync</c> scenario.
/// </remarks>
public interface IXConnectClient
{
    /// <summary>Create, delete, read and search Posts.</summary>
    IXPostsClient Posts { get; }

    /// <summary>Look up users, followers and following.</summary>
    IXUsersClient Users { get; }

    /// <summary>Upload images, GIFs and videos.</summary>
    IXMediaClient Media { get; }

    /// <summary>Likes, reposts, bookmarks, follows, blocks and mutes.</summary>
    IXEngagementClient Engagement { get; }

    /// <summary>Direct Messages.</summary>
    IXDirectMessageClient DirectMessages { get; }

    /// <summary>Lists.</summary>
    IXListsClient Lists { get; }

    /// <summary>Spaces.</summary>
    IXSpacesClient Spaces { get; }

    /// <summary>Sample and filtered streams, and stream rules.</summary>
    IXStreamingClient Streaming { get; }

    /// <summary>Batch compliance jobs.</summary>
    IXComplianceClient Compliance { get; }

    /// <summary>Trends.</summary>
    IXTrendsClient Trends { get; }

    /// <summary>Communities.</summary>
    IXCommunitiesClient Communities { get; }

    /// <summary>News stories.</summary>
    IXNewsClient News { get; }

    /// <summary>Webhook registrations and Account Activity subscriptions (management calls only).</summary>
    IXWebhooksClient Webhooks { get; }

    /// <summary>Streaming connection history and termination.</summary>
    IXConnectionsClient Connections { get; }

    /// <summary>API usage and credit balances.</summary>
    IXUsageClient Usage { get; }

    /// <summary>The latest rate-limit headers seen per endpoint.</summary>
    XRateLimitTracker RateLimits { get; }
}

/// <summary>Default <see cref="IXConnectClient"/>.</summary>
public sealed class XConnectClient : IXConnectClient
{
    /// <summary>Creates a client.</summary>
    /// <param name="httpClient">A long-lived <see cref="HttpClient"/> (or one from <c>IHttpClientFactory</c>). Its <c>BaseAddress</c> is ignored in favour of <see cref="XConnectOptions.BaseAddress"/>.</param>
    /// <param name="options">Authentication, base address, timeouts and retry settings.</param>
    /// <param name="timeProvider">Clock for OAuth timestamps, retry delays and token expiry.</param>
    /// <param name="logger">Optional logger. Tokens, secrets, headers and bodies are never logged.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public XConnectClient(HttpClient httpClient, XConnectOptions options, TimeProvider? timeProvider = null, ILogger<XConnectClient>? logger = null)
        : this(httpClient, options, XConnectShared.Create(httpClient, options, timeProvider), timeProvider, logger)
    {
    }

    internal XConnectClient(HttpClient httpClient, XConnectOptions options, XConnectShared shared, TimeProvider? timeProvider, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(shared);

        RateLimits = shared.RateLimits;
        var transport = new XApiTransport(httpClient, options, shared.Authenticators, RateLimits, timeProvider, logger);
        var resolver = new XUserIdResolver(transport);

        Posts = new XPostsClient(transport);
        Users = new XUsersClient(transport);
        Media = new XMediaClient(transport);
        Engagement = new XEngagementClient(transport, resolver);
        DirectMessages = new XDirectMessageClient(transport);
        Lists = new XListsClient(transport, resolver);
        Spaces = new XSpacesClient(transport);
        Streaming = new XStreamingClient(transport);
        Compliance = new XComplianceClient(transport);
        Trends = new XTrendsClient(transport);
        Communities = new XCommunitiesClient(transport);
        News = new XNewsClient(transport);
        Webhooks = new XWebhooksClient(transport);
        Connections = new XConnectionsClient(transport);
        Usage = new XUsageClient(transport);
    }

    /// <inheritdoc />
    public IXPostsClient Posts { get; }

    /// <inheritdoc />
    public IXUsersClient Users { get; }

    /// <inheritdoc />
    public IXMediaClient Media { get; }

    /// <inheritdoc />
    public IXEngagementClient Engagement { get; }

    /// <inheritdoc />
    public IXDirectMessageClient DirectMessages { get; }

    /// <inheritdoc />
    public IXListsClient Lists { get; }

    /// <inheritdoc />
    public IXSpacesClient Spaces { get; }

    /// <inheritdoc />
    public IXStreamingClient Streaming { get; }

    /// <inheritdoc />
    public IXComplianceClient Compliance { get; }

    /// <inheritdoc />
    public IXTrendsClient Trends { get; }

    /// <inheritdoc />
    public IXCommunitiesClient Communities { get; }

    /// <inheritdoc />
    public IXNewsClient News { get; }

    /// <inheritdoc />
    public IXWebhooksClient Webhooks { get; }

    /// <inheritdoc />
    public IXConnectionsClient Connections { get; }

    /// <inheritdoc />
    public IXUsageClient Usage { get; }

    /// <inheritdoc />
    public XRateLimitTracker RateLimits { get; }
}
