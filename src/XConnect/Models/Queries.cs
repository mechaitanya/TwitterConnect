namespace XConnect;

/// <summary>
/// Chooses which optional fields and expansions X returns. Nothing is requested unless you ask for it,
/// which keeps responses small. See <see cref="XPostFields"/>, <see cref="XUserFields"/> and the other constant classes.
/// </summary>
public sealed record XFieldSelection
{
    /// <summary>Values for <c>post.fields</c>.</summary>
    public IReadOnlyList<string>? PostFields { get; init; }

    /// <summary>Values for <c>user.fields</c>.</summary>
    public IReadOnlyList<string>? UserFields { get; init; }

    /// <summary>Values for <c>media.fields</c>.</summary>
    public IReadOnlyList<string>? MediaFields { get; init; }

    /// <summary>Values for <c>poll.fields</c>.</summary>
    public IReadOnlyList<string>? PollFields { get; init; }

    /// <summary>Values for <c>place.fields</c>.</summary>
    public IReadOnlyList<string>? PlaceFields { get; init; }

    /// <summary>Values for <c>list.fields</c>.</summary>
    public IReadOnlyList<string>? ListFields { get; init; }

    /// <summary>Values for <c>space.fields</c>.</summary>
    public IReadOnlyList<string>? SpaceFields { get; init; }

    /// <summary>Values for <c>dm_event.fields</c>.</summary>
    public IReadOnlyList<string>? DmEventFields { get; init; }

    /// <summary>Values for <c>community.fields</c>.</summary>
    public IReadOnlyList<string>? CommunityFields { get; init; }

    /// <summary>Values for <c>expansions</c>.</summary>
    public IReadOnlyList<string>? Expansions { get; init; }
}

/// <summary>Common options for paged endpoints that return Posts.</summary>
public sealed record XPostQuery
{
    /// <summary>Fields and expansions.</summary>
    public XFieldSelection? Fields { get; init; }

    /// <summary>Page size. Allowed range depends on the endpoint (most accept 5 to 100).</summary>
    public int? MaxResults { get; init; }

    /// <summary>Token from the previous page's <see cref="XResponse{T}.NextToken"/>.</summary>
    public string? PaginationToken { get; init; }

    /// <summary>Oldest UTC time to return.</summary>
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>Newest UTC time to return.</summary>
    public DateTimeOffset? EndTime { get; init; }

    /// <summary>Return only Posts newer than this ID.</summary>
    public string? SinceId { get; init; }

    /// <summary>Return only Posts older than this ID.</summary>
    public string? UntilId { get; init; }

    /// <summary>Exclude <c>replies</c> and/or <c>retweets</c> (where the endpoint supports it).</summary>
    public IReadOnlyList<string>? Exclude { get; init; }
}

/// <summary>Options for Post search.</summary>
public sealed record XSearchQuery
{
    /// <summary>Fields and expansions.</summary>
    public XFieldSelection? Fields { get; init; }

    /// <summary>Page size: 10 to 100 for recent search, 10 to 500 for full-archive search.</summary>
    public int? MaxResults { get; init; }

    /// <summary>Token from the previous page.</summary>
    public string? PaginationToken { get; init; }

    /// <summary>Oldest UTC time.</summary>
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>Newest UTC time.</summary>
    public DateTimeOffset? EndTime { get; init; }

    /// <summary>Return only Posts newer than this ID.</summary>
    public string? SinceId { get; init; }

    /// <summary>Return only Posts older than this ID.</summary>
    public string? UntilId { get; init; }

    /// <summary>Either <c>recency</c> or <c>relevancy</c>.</summary>
    public string? SortOrder { get; init; }
}

/// <summary>Options for Post counts.</summary>
public sealed record XCountsQuery
{
    /// <summary>Bucket size: <c>minute</c>, <c>hour</c> or <c>day</c>.</summary>
    public string? Granularity { get; init; }

    /// <summary>Oldest UTC time.</summary>
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>Newest UTC time.</summary>
    public DateTimeOffset? EndTime { get; init; }

    /// <summary>Return only Posts newer than this ID.</summary>
    public string? SinceId { get; init; }

    /// <summary>Return only Posts older than this ID.</summary>
    public string? UntilId { get; init; }

    /// <summary>Token from the previous page.</summary>
    public string? PaginationToken { get; init; }
}

/// <summary>Options for paged endpoints that return users, Lists, Spaces or similar objects.</summary>
public sealed record XPageQuery
{
    /// <summary>Fields and expansions.</summary>
    public XFieldSelection? Fields { get; init; }

    /// <summary>Page size. Allowed range depends on the endpoint.</summary>
    public int? MaxResults { get; init; }

    /// <summary>Token from the previous page.</summary>
    public string? PaginationToken { get; init; }
}

/// <summary>Values for <c>post.fields</c>, taken from the X API OpenAPI specification.</summary>
public static class XPostFields
{
    /// <summary>The <c>attachments</c> field.</summary>
    public const string Attachments = "attachments";

    /// <summary>The <c>context_annotations</c> field.</summary>
    public const string ContextAnnotations = "context_annotations";

    /// <summary>The <c>conversation_id</c> field.</summary>
    public const string ConversationId = "conversation_id";

    /// <summary>The <c>created_at</c> field.</summary>
    public const string CreatedAt = "created_at";

    /// <summary>The <c>edit_controls</c> field.</summary>
    public const string EditControls = "edit_controls";

    /// <summary>The <c>entities</c> field.</summary>
    public const string Entities = "entities";

    /// <summary>The <c>geo</c> field.</summary>
    public const string Geo = "geo";

    /// <summary>The <c>lang</c> field.</summary>
    public const string Lang = "lang";

    /// <summary>The <c>public_metrics</c> field.</summary>
    public const string PublicMetrics = "public_metrics";

    /// <summary>The <c>non_public_metrics</c> field (owner, user context, recent Posts only).</summary>
    public const string NonPublicMetrics = "non_public_metrics";

    /// <summary>The <c>organic_metrics</c> field (owner, user context).</summary>
    public const string OrganicMetrics = "organic_metrics";

    /// <summary>The <c>possibly_sensitive</c> field.</summary>
    public const string PossiblySensitive = "possibly_sensitive";

    /// <summary>The <c>reply_settings</c> field.</summary>
    public const string ReplySettings = "reply_settings";

    /// <summary>The <c>source</c> field.</summary>
    public const string Source = "source";

    /// <summary>The <c>withheld</c> field.</summary>
    public const string Withheld = "withheld";
}

/// <summary>Values for <c>user.fields</c>.</summary>
public static class XUserFields
{
    /// <summary>The <c>created_at</c> field.</summary>
    public const string CreatedAt = "created_at";

    /// <summary>The <c>description</c> field.</summary>
    public const string Description = "description";

    /// <summary>The <c>entities</c> field.</summary>
    public const string Entities = "entities";

    /// <summary>The <c>location</c> field.</summary>
    public const string Location = "location";

    /// <summary>The <c>pinned_post_id</c> field.</summary>
    public const string PinnedPostId = "pinned_post_id";

    /// <summary>The <c>profile_image_url</c> field.</summary>
    public const string ProfileImageUrl = "profile_image_url";

    /// <summary>The <c>protected</c> field.</summary>
    public const string Protected = "protected";

    /// <summary>The <c>public_metrics</c> field (followers, following, post and listed counts).</summary>
    public const string PublicMetrics = "public_metrics";

    /// <summary>The <c>url</c> field.</summary>
    public const string Url = "url";

    /// <summary>The <c>verified</c> field.</summary>
    public const string Verified = "verified";

    /// <summary>The <c>verified_type</c> field.</summary>
    public const string VerifiedType = "verified_type";

    /// <summary>The <c>withheld</c> field.</summary>
    public const string Withheld = "withheld";
}

/// <summary>Values for <c>media.fields</c>.</summary>
public static class XMediaFields
{
    /// <summary>The <c>alt_text</c> field.</summary>
    public const string AltText = "alt_text";

    /// <summary>The <c>duration_ms</c> field.</summary>
    public const string DurationMs = "duration_ms";

    /// <summary>The <c>height</c> field.</summary>
    public const string Height = "height";

    /// <summary>The <c>media_key</c> field.</summary>
    public const string MediaKey = "media_key";

    /// <summary>The <c>preview_image_url</c> field.</summary>
    public const string PreviewImageUrl = "preview_image_url";

    /// <summary>The <c>type</c> field.</summary>
    public const string Type = "type";

    /// <summary>The <c>url</c> field.</summary>
    public const string Url = "url";

    /// <summary>The <c>variants</c> field.</summary>
    public const string Variants = "variants";

    /// <summary>The <c>width</c> field.</summary>
    public const string Width = "width";
}

/// <summary>Values for <c>expansions</c> on Post endpoints.</summary>
public static class XPostExpansions
{
    /// <summary>Expands the author.</summary>
    public const string AuthorId = "author_id";

    /// <summary>Expands attached media.</summary>
    public const string MediaKeys = "attachments.media_keys";

    /// <summary>Expands attached polls.</summary>
    public const string PollIds = "attachments.poll_ids";

    /// <summary>Expands the place.</summary>
    public const string PlaceId = "geo.place_id";

    /// <summary>Expands the user being replied to.</summary>
    public const string InReplyToUserId = "in_reply_to_user_id";

    /// <summary>Expands referenced Posts.</summary>
    public const string ReferencedPosts = "referenced_posts";

    /// <summary>Expands mentioned users.</summary>
    public const string MentionsUsername = "entities.mentions.username";
}

/// <summary>Values for <c>expansions</c> on user endpoints.</summary>
public static class XUserExpansions
{
    /// <summary>Expands the pinned Post.</summary>
    public const string PinnedPostId = "pinned_post_id";

    /// <summary>Expands the most recent Post.</summary>
    public const string MostRecentPostId = "most_recent_post_id";
}
