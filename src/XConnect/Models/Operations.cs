namespace XConnect.Models;

/// <summary>A streaming connection of the authenticated application (<c>GET /2/connections</c>).</summary>
public sealed record XConnection
{
    /// <summary>The connection ID (the UUID you pass to <see cref="IXConnectionsClient.TerminateAsync"/>).</summary>
    public string? Id { get; init; }

    /// <summary>The streaming endpoint, for example <c>filtered_stream</c>.</summary>
    public string? EndpointName { get; init; }

    /// <summary>The client IP address as seen by X.</summary>
    public string? ClientIp { get; init; }

    /// <summary>When the connection opened (RFC 3339 text, as returned).</summary>
    public string? ConnectedAt { get; init; }

    /// <summary>When it closed, if it has.</summary>
    public string? DisconnectedAt { get; init; }

    /// <summary>The reason X gave for the disconnect, if any.</summary>
    public string? DisconnectReason { get; init; }
}

/// <summary>Values for <c>connection.fields</c>.</summary>
public static class XConnectionFields
{
    /// <summary><c>client_ip</c>.</summary>
    public const string ClientIp = "client_ip";

    /// <summary><c>connected_at</c>.</summary>
    public const string ConnectedAt = "connected_at";

    /// <summary><c>disconnect_reason</c>.</summary>
    public const string DisconnectReason = "disconnect_reason";

    /// <summary><c>disconnected_at</c>.</summary>
    public const string DisconnectedAt = "disconnected_at";

    /// <summary><c>endpoint_name</c>.</summary>
    public const string EndpointName = "endpoint_name";

    /// <summary><c>id</c>.</summary>
    public const string Id = "id";
}

/// <summary>Streaming endpoint IDs accepted by <see cref="IXConnectionsClient.TerminateByEndpointAsync"/> (the spec's closed list).</summary>
public static class XStreamEndpoints
{
    /// <summary>The filtered stream.</summary>
    public const string FilteredStream = "filtered_stream";

    /// <summary>The 1% sample stream.</summary>
    public const string SampleStream = "sample_stream";

    /// <summary>The 10% sample stream.</summary>
    public const string Sample10Stream = "sample10_stream";

    /// <summary>The firehose stream.</summary>
    public const string FirehoseStream = "firehose_stream";

    /// <summary>The Post compliance stream.</summary>
    public const string TweetsComplianceStream = "tweets_compliance_stream";

    /// <summary>The user compliance stream.</summary>
    public const string UsersComplianceStream = "users_compliance_stream";

    /// <summary>The Post label stream.</summary>
    public const string TweetLabelStream = "tweet_label_stream";

    /// <summary>The English firehose partition.</summary>
    public const string FirehoseStreamLangEn = "firehose_stream_lang_en";

    /// <summary>The Japanese firehose partition.</summary>
    public const string FirehoseStreamLangJa = "firehose_stream_lang_ja";

    /// <summary>The Korean firehose partition.</summary>
    public const string FirehoseStreamLangKo = "firehose_stream_lang_ko";

    /// <summary>The Portuguese firehose partition.</summary>
    public const string FirehoseStreamLangPt = "firehose_stream_lang_pt";

    /// <summary>The likes firehose stream.</summary>
    public const string LikesFirehoseStream = "likes_firehose_stream";

    /// <summary>The likes 10% sample stream.</summary>
    public const string LikesSample10Stream = "likes_sample10_stream";

    /// <summary>The likes compliance stream.</summary>
    public const string LikesComplianceStream = "likes_compliance_stream";

    internal static readonly HashSet<string> All =
    [
        FilteredStream, SampleStream, Sample10Stream, FirehoseStream, TweetsComplianceStream, UsersComplianceStream, TweetLabelStream,
        FirehoseStreamLangEn, FirehoseStreamLangJa, FirehoseStreamLangKo, FirehoseStreamLangPt, LikesFirehoseStream, LikesSample10Stream, LikesComplianceStream,
    ];
}

/// <summary>Outcome of terminating streaming connections.</summary>
public sealed record XConnectionKillSummary
{
    /// <summary>How many connections were terminated.</summary>
    public int SuccessfulKills { get; init; }

    /// <summary>How many could not be terminated.</summary>
    public int FailedKills { get; init; }

    /// <summary>Per-connection results.</summary>
    public IReadOnlyList<XConnectionKillResult>? Results { get; init; }
}

/// <summary>The result for one connection.</summary>
public sealed record XConnectionKillResult
{
    /// <summary>The connection UUID.</summary>
    public string? Uuid { get; init; }

    /// <summary>Whether it was terminated.</summary>
    public bool Success { get; init; }

    /// <summary>Why it failed, when it did.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>A registered webhook (<c>/2/webhooks</c>).</summary>
public sealed record XWebhookConfig
{
    /// <summary>The webhook ID.</summary>
    public string? Id { get; init; }

    /// <summary>The callback URL.</summary>
    public string? Url { get; init; }

    /// <summary>Whether the last CRC check passed.</summary>
    public bool? Valid { get; init; }

    /// <summary>When it was created (RFC 3339 text, as returned).</summary>
    public string? CreatedAt { get; init; }
}

/// <summary>The outcome of a CRC re-validation (<c>PUT /2/webhooks/{id}</c>).</summary>
public sealed record XWebhookValidation
{
    /// <summary>True when your endpoint answered the challenge correctly.</summary>
    public bool Valid { get; init; }
}

/// <summary>A replay job for missed webhook events.</summary>
public sealed record XWebhookReplayJob
{
    /// <summary>The job ID.</summary>
    public string? JobId { get; init; }

    /// <summary>When it was created.</summary>
    public string? CreatedAt { get; init; }
}

/// <summary>A webhook that is linked to the filtered stream (<c>/2/tweets/search/webhooks</c>).</summary>
public sealed record XWebhookStreamLink
{
    /// <summary>The webhook ID.</summary>
    public string? WebhookId { get; init; }

    /// <summary>The application ID.</summary>
    public string? ApplicationId { get; init; }

    /// <summary>The business user ID.</summary>
    public string? BusinessUserId { get; init; }

    /// <summary>The instance ID.</summary>
    public string? InstanceId { get; init; }

    /// <summary>When the link was created.</summary>
    public string? CreatedAt { get; init; }

    /// <summary>The requested fields, as returned.</summary>
    public IReadOnlyList<string>? Fields { get; init; }
}

/// <summary>Result of linking a webhook to the filtered stream.</summary>
public sealed record XWebhookLinkResult
{
    /// <summary>True when the link was provisioned.</summary>
    public bool Provisioned { get; init; }
}

/// <summary>Result of removing a webhook or a stream link.</summary>
public sealed record XWebhookDeleteResult
{
    /// <summary>True when removed.</summary>
    public bool Deleted { get; init; }
}

/// <summary>Whether a user is subscribed to a webhook's Account Activity events.</summary>
public sealed record XAccountActivitySubscription
{
    /// <summary>True when subscribed.</summary>
    public bool Subscribed { get; init; }
}

/// <summary>All subscriptions of a webhook.</summary>
public sealed record XAccountActivitySubscriptionList
{
    /// <summary>The application ID.</summary>
    public string? ApplicationId { get; init; }

    /// <summary>The webhook ID.</summary>
    public string? WebhookId { get; init; }

    /// <summary>The webhook URL.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>Subscribed users.</summary>
    public IReadOnlyList<XAccountActivitySubscriber>? Subscriptions { get; init; }
}

/// <summary>A subscribed user.</summary>
public sealed record XAccountActivitySubscriber
{
    /// <summary>The user ID.</summary>
    public string? UserId { get; init; }
}

/// <summary>Subscription usage for the application. The API documents these counts as strings.</summary>
public sealed record XAccountActivityCount
{
    /// <summary>The account name.</summary>
    public string? AccountName { get; init; }

    /// <summary>Subscriptions provisioned for the account.</summary>
    public string? ProvisionedCount { get; init; }

    /// <summary>All active subscriptions.</summary>
    public string? SubscriptionsCountAll { get; init; }

    /// <summary>Active Direct Message subscriptions.</summary>
    public string? SubscriptionsCountDirectMessages { get; init; }
}
