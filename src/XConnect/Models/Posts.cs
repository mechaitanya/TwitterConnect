using System.Text.Json;
using System.Text.Json.Serialization;

namespace XConnect.Models;

/// <summary>A Post. Only the fields requested through <c>post.fields</c> (plus <c>id</c> and <c>text</c>) are populated.</summary>
public sealed record XPost
{
    /// <summary>The Post ID. IDs are strings because they exceed the range of JSON numbers.</summary>
    public string? Id { get; init; }

    /// <summary>The Post text.</summary>
    public string? Text { get; init; }

    /// <summary>Author user ID.</summary>
    public string? AuthorId { get; init; }

    /// <summary>When the Post was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>ID of the conversation (the root Post's ID).</summary>
    public string? ConversationId { get; init; }

    /// <summary>User ID this Post replies to.</summary>
    public string? InReplyToUserId { get; init; }

    /// <summary>Language code detected by X.</summary>
    public string? Lang { get; init; }

    /// <summary>The client that posted this Post.</summary>
    public string? Source { get; init; }

    /// <summary>Who can reply.</summary>
    public string? ReplySettings { get; init; }

    /// <summary>True when the Post may contain sensitive content.</summary>
    public bool? PossiblySensitive { get; init; }

    /// <summary>IDs of the edit history chain.</summary>
    public IReadOnlyList<string>? EditHistoryPostIds { get; init; }

    /// <summary>Attachment references.</summary>
    public XPostAttachments? Attachments { get; init; }

    /// <summary>Entities parsed from the text.</summary>
    public XPostEntities? Entities { get; init; }

    /// <summary>Public engagement counts.</summary>
    public XPostPublicMetrics? PublicMetrics { get; init; }

    /// <summary>Posts this Post replies to, quotes or reposts.</summary>
    public IReadOnlyList<XReferencedPost>? ReferencedPosts { get; init; }

    /// <summary>Edit eligibility.</summary>
    public XPostEditControls? EditControls { get; init; }

    /// <summary>Attached place.</summary>
    public XPostGeo? Geo { get; init; }

    /// <summary>Context annotations.</summary>
    public IReadOnlyList<XContextAnnotation>? ContextAnnotations { get; init; }

    /// <summary>Withholding information.</summary>
    public XWithheld? Withheld { get; init; }

    /// <summary>The URI of the Post's card, if it has one.</summary>
    public string? CardUri { get; init; }

    /// <summary>The Community the Post was posted in.</summary>
    public string? CommunityId { get; init; }

    /// <summary>True for paid partnership content.</summary>
    public bool? PaidPartnership { get; init; }

    /// <summary>The start and end offsets of the text to display (the rest is leading reply handles or trailing media links).</summary>
    public IReadOnlyList<int>? DisplayTextRange { get; init; }

    /// <summary>Who may see the Post when it is limited to a group.</summary>
    public XPostScopes? Scopes { get; init; }

    /// <summary>The long-form text of a Note Post.</summary>
    public XNotePost? NotePost { get; init; }

    /// <summary>Community Notes matched to the Post's media.</summary>
    public IReadOnlyList<XMatchedMediaNote>? MatchedMediaNotes { get; init; }

    /// <summary>Per-media alt text and titles.</summary>
    public IReadOnlyList<XPostMediaMetadata>? MediaMetadata { get; init; }

    /// <summary>Suggestions for Community Note requests.</summary>
    public IReadOnlyList<XNoteRequestSuggestion>? NoteRequestSuggestions { get; init; }

    /// <summary>Suggested source links.</summary>
    public IReadOnlyList<string>? SuggestedSourceLinks { get; init; }

    /// <summary>Suggested source links with how often each was suggested.</summary>
    public IReadOnlyList<XSourceLinkCount>? SuggestedSourceLinksWithCounts { get; init; }

    /// <summary>The article attached to the Post. The specification leaves the shape undefined, so it is exposed as raw JSON.</summary>
    public JsonElement? Article { get; init; }

    /// <summary>The article title. The specification leaves the shape undefined.</summary>
    public JsonElement? ArticleTitle { get; init; }

    /// <summary>Metrics visible only to the Post's owner (requires user context). The specification leaves the shape undefined.</summary>
    public JsonElement? NonPublicMetrics { get; init; }

    /// <summary>Organic metrics (requires user context). The specification leaves the shape undefined.</summary>
    public JsonElement? OrganicMetrics { get; init; }

    /// <summary>Promoted metrics (requires user context). The specification leaves the shape undefined.</summary>
    public JsonElement? PromotedMetrics { get; init; }

    /// <summary>The author's username, when the API returns it with the Post.</summary>
    public string? Username { get; init; }
}

/// <summary>Media, poll and source references of a Post.</summary>
public sealed record XPostAttachments
{
    /// <summary>Media keys.</summary>
    public IReadOnlyList<string>? MediaKeys { get; init; }

    /// <summary>Poll IDs.</summary>
    public IReadOnlyList<string>? PollIds { get; init; }

    /// <summary>IDs of the Posts the attached media originally came from (<c>media_source_tweet_id</c> on the wire).</summary>
    [JsonPropertyName("media_source_tweet_id")]
    public IReadOnlyList<string>? MediaSourcePostIds { get; init; }
}

/// <summary>Entities in a Post.</summary>
public sealed record XPostEntities
{
    /// <summary>Links.</summary>
    public IReadOnlyList<XUrlEntity>? Urls { get; init; }

    /// <summary>Hashtags.</summary>
    public IReadOnlyList<XTagEntity>? Hashtags { get; init; }

    /// <summary>Cashtags.</summary>
    public IReadOnlyList<XTagEntity>? Cashtags { get; init; }

    /// <summary>Mentions.</summary>
    public IReadOnlyList<XMentionEntity>? Mentions { get; init; }
}

/// <summary>A link entity.</summary>
public sealed record XUrlEntity
{
    /// <summary>Start offset.</summary>
    public int Start { get; init; }

    /// <summary>End offset.</summary>
    public int End { get; init; }

    /// <summary>The t.co URL.</summary>
    public string? Url { get; init; }

    /// <summary>The expanded URL.</summary>
    public string? ExpandedUrl { get; init; }

    /// <summary>The display URL.</summary>
    public string? DisplayUrl { get; init; }

    /// <summary>Page title.</summary>
    public string? Title { get; init; }

    /// <summary>Page description.</summary>
    public string? Description { get; init; }

    /// <summary>Media key when the link is a media item.</summary>
    public string? MediaKey { get; init; }

    /// <summary>Images X found on the linked page.</summary>
    public IReadOnlyList<XUrlImage>? Images { get; init; }

    /// <summary>The HTTP status X got when resolving the link.</summary>
    public int? Status { get; init; }

    /// <summary>The URL after following redirects.</summary>
    public string? UnwoundUrl { get; init; }
}

/// <summary>A hashtag or cashtag entity.</summary>
public sealed record XTagEntity
{
    /// <summary>Start offset.</summary>
    public int Start { get; init; }

    /// <summary>End offset.</summary>
    public int End { get; init; }

    /// <summary>The tag without its leading symbol.</summary>
    public string? Tag { get; init; }
}

/// <summary>A mention entity.</summary>
public sealed record XMentionEntity
{
    /// <summary>Start offset.</summary>
    public int Start { get; init; }

    /// <summary>End offset.</summary>
    public int End { get; init; }

    /// <summary>The mentioned username.</summary>
    public string? Username { get; init; }

    /// <summary>The mentioned user ID.</summary>
    public string? Id { get; init; }
}

/// <summary>Public Post metrics.</summary>
public sealed record XPostPublicMetrics
{
    /// <summary>Reposts.</summary>
    public long? RepostCount { get; init; }

    /// <summary>Replies.</summary>
    public long? ReplyCount { get; init; }

    /// <summary>Likes.</summary>
    public long? LikeCount { get; init; }

    /// <summary>Quotes.</summary>
    public long? QuoteCount { get; init; }

    /// <summary>Bookmarks.</summary>
    public long? BookmarkCount { get; init; }

    /// <summary>Impressions.</summary>
    public long? ImpressionCount { get; init; }
}

/// <summary>A reference from one Post to another.</summary>
public sealed record XReferencedPost
{
    /// <summary>One of <c>retweeted</c>, <c>quoted</c> or <c>replied_to</c>.</summary>
    public string? Type { get; init; }

    /// <summary>The referenced Post ID.</summary>
    public string? Id { get; init; }
}

/// <summary>Edit eligibility of a Post.</summary>
public sealed record XPostEditControls
{
    /// <summary>Whether the Post can still be edited.</summary>
    public bool? IsEditEligible { get; init; }

    /// <summary>Edits remaining.</summary>
    public int? EditsRemaining { get; init; }

    /// <summary>When editing stops being possible.</summary>
    public DateTimeOffset? EditableUntil { get; init; }
}

/// <summary>Location attached to a Post.</summary>
public sealed record XPostGeo
{
    /// <summary>The place ID.</summary>
    public string? PlaceId { get; init; }

    /// <summary>The exact coordinates, when shared.</summary>
    public XPostCoordinates? Coordinates { get; init; }
}

/// <summary>A context annotation.</summary>
public sealed record XContextAnnotation
{
    /// <summary>The annotation domain.</summary>
    public XAnnotationEntity? Domain { get; init; }

    /// <summary>The annotation entity.</summary>
    public XAnnotationEntity? Entity { get; init; }
}

/// <summary>A context annotation domain or entity.</summary>
public sealed record XAnnotationEntity
{
    /// <summary>ID.</summary>
    public string? Id { get; init; }

    /// <summary>Name.</summary>
    public string? Name { get; init; }

    /// <summary>Description.</summary>
    public string? Description { get; init; }
}

/// <summary>Withholding details for a Post or user.</summary>
public sealed record XWithheld
{
    /// <summary>True when withheld for copyright.</summary>
    public bool? Copyright { get; init; }

    /// <summary>Countries where the content is withheld.</summary>
    public IReadOnlyList<string>? CountryCodes { get; init; }

    /// <summary>The scope: <c>post</c> or <c>user</c>.</summary>
    public string? Scope { get; init; }
}

/// <summary>The created Post returned by <c>POST /2/tweets</c>.</summary>
public sealed record XCreatedPost
{
    /// <summary>The new Post ID.</summary>
    public required string Id { get; init; }

    /// <summary>The Post text.</summary>
    public required string Text { get; init; }

    /// <summary>The edit history chain.</summary>
    public IReadOnlyList<string>? EditHistoryPostIds { get; init; }
}

/// <summary>Result of deleting a Post.</summary>
public sealed record XDeleteResult
{
    /// <summary>True when the Post was deleted.</summary>
    public bool Deleted { get; init; }
}

/// <summary>Result of hiding or unhiding a reply.</summary>
public sealed record XHiddenResult
{
    /// <summary>True when the reply is now hidden.</summary>
    public bool Hidden { get; init; }
}

/// <summary>One bucket of a Post counts query.</summary>
public sealed record XPostCount
{
    /// <summary>Bucket start.</summary>
    public DateTimeOffset Start { get; init; }

    /// <summary>Bucket end.</summary>
    public DateTimeOffset End { get; init; }

    /// <summary>Number of matching Posts in the bucket.</summary>
    [JsonPropertyName("post_count")]
    public long PostCount { get; init; }
}

/// <summary>A poll expanded through <c>attachments.poll_ids</c>.</summary>
public sealed record XPoll
{
    /// <summary>Poll ID.</summary>
    public string? Id { get; init; }

    /// <summary>The options and vote counts.</summary>
    public IReadOnlyList<XPollOption>? Options { get; init; }

    /// <summary>Poll length in minutes.</summary>
    public int? DurationMinutes { get; init; }

    /// <summary>End time.</summary>
    public DateTimeOffset? EndDatetime { get; init; }

    /// <summary>Voting status.</summary>
    public string? VotingStatus { get; init; }
}

/// <summary>A poll option.</summary>
public sealed record XPollOption
{
    /// <summary>1-based position.</summary>
    public int Position { get; init; }

    /// <summary>Label.</summary>
    public string? Label { get; init; }

    /// <summary>Votes.</summary>
    public long Votes { get; init; }
}

/// <summary>A place expanded through <c>geo.place_id</c>.</summary>
public sealed record XPlace
{
    /// <summary>Place ID.</summary>
    public string? Id { get; init; }

    /// <summary>Full name.</summary>
    public string? FullName { get; init; }

    /// <summary>Short name.</summary>
    public string? Name { get; init; }

    /// <summary>Country.</summary>
    public string? Country { get; init; }

    /// <summary>Country code.</summary>
    public string? CountryCode { get; init; }

    /// <summary>Place type.</summary>
    public string? PlaceType { get; init; }
}

/// <summary>An image X found on a linked page.</summary>
public sealed record XUrlImage
{
    /// <summary>Height in pixels.</summary>
    public int? Height { get; init; }

    /// <summary>Width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>The image URL.</summary>
    public string? Url { get; init; }
}

/// <summary>A point shared with a Post.</summary>
public sealed record XPostCoordinates
{
    /// <summary>The geometry type, <c>Point</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Longitude then latitude.</summary>
    public IReadOnlyList<double>? Coordinates { get; init; }
}

/// <summary>Audience limits of a Post.</summary>
public sealed record XPostScopes
{
    /// <summary>True when only followers may see the Post.</summary>
    public bool? Followers { get; init; }
}

/// <summary>The long-form text of a Note Post.</summary>
public sealed record XNotePost
{
    /// <summary>The full text.</summary>
    public string? Text { get; init; }

    /// <summary>Entities in the text.</summary>
    public XPostEntities? Entities { get; init; }
}

/// <summary>A Community Note matched to a Post's media.</summary>
public sealed record XMatchedMediaNote
{
    /// <summary>The match status, as returned.</summary>
    public string? MatchStatus { get; init; }

    /// <summary>The Note's ID.</summary>
    public string? NoteId { get; init; }
}

/// <summary>Alt text and title for one media item of a Post.</summary>
public sealed record XPostMediaMetadata
{
    /// <summary>The media key.</summary>
    public string? MediaKey { get; init; }

    /// <summary>The alt text.</summary>
    public string? AltText { get; init; }

    /// <summary>The description.</summary>
    public string? Description { get; init; }

    /// <summary>The title.</summary>
    public string? Title { get; init; }
}

/// <summary>A suggested Community Note request.</summary>
public sealed record XNoteRequestSuggestion
{
    /// <summary>The suggestion ID.</summary>
    public string? SuggestionId { get; init; }

    /// <summary>The suggested text.</summary>
    public string? Suggestion { get; init; }

    /// <summary>The suggested source link.</summary>
    public string? SourceLink { get; init; }
}

/// <summary>A suggested source link and how many times it was suggested.</summary>
public sealed record XSourceLinkCount
{
    /// <summary>The URL.</summary>
    public string? Url { get; init; }

    /// <summary>The count.</summary>
    public int? Count { get; init; }
}
