namespace XConnect.Models;

/// <summary>An X List.</summary>
public sealed record XList
{
    /// <summary>List ID.</summary>
    public string? Id { get; init; }

    /// <summary>Name.</summary>
    public string? Name { get; init; }

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Owner user ID.</summary>
    public string? OwnerId { get; init; }

    /// <summary>True when private.</summary>
    public bool? Private { get; init; }

    /// <summary>Members.</summary>
    public int? MemberCount { get; init; }

    /// <summary>Followers.</summary>
    public int? FollowerCount { get; init; }

    /// <summary>Creation time.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>Fields that create or update a List.</summary>
public sealed record XListRequest
{
    /// <summary>Name. Required when creating.</summary>
    public string? Name { get; init; }

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the List is private.</summary>
    public bool? Private { get; init; }
}

/// <summary>Result of updating a List.</summary>
public sealed record XListUpdateResult
{
    /// <summary>True when updated.</summary>
    public bool Updated { get; init; }
}

/// <summary>Result of deleting a List.</summary>
public sealed record XListDeleteResult
{
    /// <summary>True when deleted.</summary>
    public bool Deleted { get; init; }
}

/// <summary>Result of adding or removing a List member.</summary>
public sealed record XListMemberResult
{
    /// <summary>True while the user is a member.</summary>
    public bool IsMember { get; init; }
}

/// <summary>Result of following or unfollowing a List.</summary>
public sealed record XListFollowResult
{
    /// <summary>True while following.</summary>
    public bool Following { get; init; }
}

/// <summary>An audio Space.</summary>
public sealed record XSpace
{
    /// <summary>Space ID.</summary>
    public string? Id { get; init; }

    /// <summary>Title.</summary>
    public string? Title { get; init; }

    /// <summary>One of <c>live</c>, <c>scheduled</c> or <c>ended</c>.</summary>
    public string? State { get; init; }

    /// <summary>Creator user ID.</summary>
    public string? CreatorId { get; init; }

    /// <summary>Host IDs.</summary>
    public IReadOnlyList<string>? HostIds { get; init; }

    /// <summary>Speaker IDs.</summary>
    public IReadOnlyList<string>? SpeakerIds { get; init; }

    /// <summary>Invited user IDs.</summary>
    public IReadOnlyList<string>? InvitedUserIds { get; init; }

    /// <summary>Language code.</summary>
    public string? Lang { get; init; }

    /// <summary>True when ticketed.</summary>
    public bool? IsTicketed { get; init; }

    /// <summary>Participant count.</summary>
    public int? ParticipantCount { get; init; }

    /// <summary>Subscriber count.</summary>
    public int? SubscriberCount { get; init; }

    /// <summary>Scheduled start.</summary>
    public DateTimeOffset? ScheduledStart { get; init; }

    /// <summary>Actual start.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>End time.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Creation time.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Last update time.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Topic IDs attached to the Space.</summary>
    public IReadOnlyList<string>? TopicIds { get; init; }
}

/// <summary>A Direct Message event.</summary>
public sealed record XDmEvent
{
    /// <summary>Event ID.</summary>
    public string? Id { get; init; }

    /// <summary>Event type: <c>MessageCreate</c>, <c>ParticipantsJoin</c> or <c>ParticipantsLeave</c>.</summary>
    public string? EventType { get; init; }

    /// <summary>Message text.</summary>
    public string? Text { get; init; }

    /// <summary>Sender user ID.</summary>
    public string? SenderId { get; init; }

    /// <summary>Conversation ID.</summary>
    public string? DmConversationId { get; init; }

    /// <summary>Creation time.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Participant IDs for join/leave events.</summary>
    public IReadOnlyList<string>? ParticipantIds { get; init; }

    /// <summary>Media and card attachments.</summary>
    public XDmAttachments? Attachments { get; init; }

    /// <summary>Entities in the text.</summary>
    public XPostEntities? Entities { get; init; }

    /// <summary>Posts referenced by the message.</summary>
    public IReadOnlyList<XDmReferencedPost>? ReferencedPosts { get; init; }
}

/// <summary>The identifiers returned after sending a Direct Message.</summary>
public sealed record XDmSendResult
{
    /// <summary>The conversation ID.</summary>
    public required string DmConversationId { get; init; }

    /// <summary>The event ID of the new message.</summary>
    public required string DmEventId { get; init; }
}

/// <summary>Result of deleting a Direct Message event.</summary>
public sealed record XDmDeleteResult
{
    /// <summary>True when deleted.</summary>
    public bool Deleted { get; init; }
}

/// <summary>A filtered stream rule.</summary>
public sealed record XStreamRule
{
    /// <summary>Rule ID (assigned by X).</summary>
    public string? Id { get; init; }

    /// <summary>The rule expression.</summary>
    public string? Value { get; init; }

    /// <summary>An optional label.</summary>
    public string? Tag { get; init; }
}

/// <summary>A rule to add.</summary>
/// <param name="Value">The filter expression.</param>
/// <param name="Tag">An optional label that is echoed in matching stream events.</param>
public sealed record XStreamRuleDraft(string Value, string? Tag = null);

/// <summary>Summary of a rule update.</summary>
public sealed record XRuleSummary
{
    /// <summary>Rules created.</summary>
    public int? Created { get; init; }

    /// <summary>Rules not created.</summary>
    public int? NotCreated { get; init; }

    /// <summary>Rules deleted.</summary>
    public int? Deleted { get; init; }

    /// <summary>Rules not deleted.</summary>
    public int? NotDeleted { get; init; }

    /// <summary>Rules that are invalid.</summary>
    public int? Invalid { get; init; }
}

/// <summary>A compliance job.</summary>
public sealed record XComplianceJob
{
    /// <summary>Job ID.</summary>
    public string? Id { get; init; }

    /// <summary>Name.</summary>
    public string? Name { get; init; }

    /// <summary>Type: <c>tweets</c> or <c>users</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Status: created, in_progress, failed or complete.</summary>
    public string? Status { get; init; }

    /// <summary>Pre-signed URL to upload the ID list.</summary>
    public string? UploadUrl { get; init; }

    /// <summary>When the upload URL expires.</summary>
    public DateTimeOffset? UploadExpiresAt { get; init; }

    /// <summary>Pre-signed URL to download results.</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>When the download URL expires.</summary>
    public DateTimeOffset? DownloadExpiresAt { get; init; }

    /// <summary>Creation time.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Whether the upload can be resumed.</summary>
    public bool? Resumable { get; init; }
}

/// <summary>A trend returned for a WOEID.</summary>
public sealed record XTrend
{
    /// <summary>Trend name.</summary>
    public string? TrendName { get; init; }

    /// <summary>Post volume.</summary>
    public long? TweetCount { get; init; }
}

/// <summary>A Community.</summary>
public sealed record XCommunity
{
    /// <summary>Community ID.</summary>
    public string? Id { get; init; }

    /// <summary>Name.</summary>
    public string? Name { get; init; }

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Access level.</summary>
    public string? Access { get; init; }

    /// <summary>Join policy.</summary>
    public string? JoinPolicy { get; init; }

    /// <summary>Members.</summary>
    public int? MemberCount { get; init; }

    /// <summary>Creation time.</summary>
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>Engagement counters shared by a Post analytics result and each of its time buckets. Only requested metrics are populated.</summary>
public record XPostAnalyticsMetrics
{
    /// <summary>App install attempts.</summary>
    public long? AppInstallAttempts { get; init; }

    /// <summary>App opens.</summary>
    public long? AppOpens { get; init; }

    /// <summary>Bookmarks.</summary>
    public long? Bookmarks { get; init; }

    /// <summary>Detail expands.</summary>
    public long? DetailExpands { get; init; }

    /// <summary>Emails of the Post.</summary>
    public long? EmailTweet { get; init; }

    /// <summary>Engagements.</summary>
    public long? Engagements { get; init; }

    /// <summary>Follows from the Post.</summary>
    public long? Follows { get; init; }

    /// <summary>Hashtag clicks.</summary>
    public long? HashtagClicks { get; init; }

    /// <summary>Impressions.</summary>
    public long? Impressions { get; init; }

    /// <summary>Likes.</summary>
    public long? Likes { get; init; }

    /// <summary>Media views.</summary>
    public long? MediaViews { get; init; }

    /// <summary>Permalink clicks.</summary>
    public long? PermalinkClicks { get; init; }

    /// <summary>Quotes.</summary>
    public long? QuoteTweets { get; init; }

    /// <summary>Replies.</summary>
    public long? Replies { get; init; }

    /// <summary>Reposts.</summary>
    public long? Retweets { get; init; }

    /// <summary>Shares.</summary>
    public long? Shares { get; init; }

    /// <summary>Unfollows.</summary>
    public long? Unfollows { get; init; }

    /// <summary>Unlikes.</summary>
    public long? Unlikes { get; init; }

    /// <summary>URL clicks.</summary>
    public long? UrlClicks { get; init; }

    /// <summary>Profile clicks.</summary>
    public long? UserProfileClicks { get; init; }
}

/// <summary>Analytics for one Post (<c>GET /2/tweets/analytics</c>): totals plus optional time buckets.</summary>
public sealed record XPostAnalytics : XPostAnalyticsMetrics
{
    /// <summary>Post ID.</summary>
    public string? Id { get; init; }

    /// <summary>Start of the window the totals cover.</summary>
    public string? Timestamp { get; init; }

    /// <summary>Per-bucket metrics, present when <c>timestamped_metrics</c> is requested.</summary>
    public IReadOnlyList<XPostAnalyticsBucket>? TimestampedMetrics { get; init; }
}

/// <summary>One time bucket of Post analytics.</summary>
public sealed record XPostAnalyticsBucket
{
    /// <summary>Start of the bucket (ISO 8601).</summary>
    public string? Timestamp { get; init; }

    /// <summary>The metrics in the bucket.</summary>
    public XPostAnalyticsMetrics? Metrics { get; init; }
}

/// <summary>Video analytics counters. The API returns an integer when the backend value parses as a number, otherwise a string, so values are kept as JSON.</summary>
public record XMediaAnalyticsMetrics
{
    /// <summary>CTA URL clicks.</summary>
    public System.Text.Json.JsonElement? CtaUrlClicks { get; init; }

    /// <summary>CTA watch clicks.</summary>
    public System.Text.Json.JsonElement? CtaWatchClicks { get; init; }

    /// <summary>Plays started from a tap.</summary>
    public System.Text.Json.JsonElement? PlayFromTap { get; init; }

    /// <summary>Playbacks that reached 25 percent.</summary>
    public System.Text.Json.JsonElement? Playback25 { get; init; }

    /// <summary>Playbacks that reached 50 percent.</summary>
    public System.Text.Json.JsonElement? Playback50 { get; init; }

    /// <summary>Playbacks that reached 75 percent.</summary>
    public System.Text.Json.JsonElement? Playback75 { get; init; }

    /// <summary>Completed playbacks.</summary>
    public System.Text.Json.JsonElement? PlaybackComplete { get; init; }

    /// <summary>Started playbacks.</summary>
    public System.Text.Json.JsonElement? PlaybackStart { get; init; }

    /// <summary>Video views.</summary>
    public System.Text.Json.JsonElement? VideoViews { get; init; }

    /// <summary>Watch time in milliseconds.</summary>
    public System.Text.Json.JsonElement? WatchTimeMs { get; init; }
}

/// <summary>Analytics for one media item (<c>GET /2/media/analytics</c>): totals plus optional time buckets.</summary>
public sealed record XMediaAnalytics : XMediaAnalyticsMetrics
{
    /// <summary>The media key.</summary>
    public string? MediaKey { get; init; }

    /// <summary>Start of the window.</summary>
    public string? Timestamp { get; init; }

    /// <summary>Per-bucket metrics, present when <c>timestamped_metrics</c> is requested.</summary>
    public IReadOnlyList<XMediaAnalyticsBucket>? TimestampedMetrics { get; init; }
}

/// <summary>One time bucket of media analytics.</summary>
public sealed record XMediaAnalyticsBucket
{
    /// <summary>Start of the bucket.</summary>
    public string? Timestamp { get; init; }

    /// <summary>The metrics in the bucket.</summary>
    public XMediaAnalyticsMetrics? Metrics { get; init; }
}

/// <summary>Attachments of a Direct Message.</summary>
public sealed record XDmAttachments
{
    /// <summary>Attached media keys.</summary>
    public IReadOnlyList<string>? MediaKeys { get; init; }

    /// <summary>Attached card IDs.</summary>
    public IReadOnlyList<string>? CardIds { get; init; }
}

/// <summary>A Post referenced by a Direct Message.</summary>
public sealed record XDmReferencedPost
{
    /// <summary>The Post ID.</summary>
    public string? Id { get; init; }
}
