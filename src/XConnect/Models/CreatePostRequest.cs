namespace XConnect.Models;

/// <summary>
/// A request to create a Post (<c>POST /2/tweets</c>). One request shape covers plain Posts, replies,
/// quotes, polls, media Posts and edits; set only the properties that apply.
/// </summary>
public sealed record CreatePostRequest
{
    /// <summary>The Post text. Required unless <see cref="Media"/> is set. Limited to 280 characters unless the account has X Premium.</summary>
    public string? Text { get; init; }

    /// <summary>Media to attach (up to 4 photos, 1 GIF or 1 video per the X documentation).</summary>
    public XPostMedia? Media { get; init; }

    /// <summary>A poll. Cannot be combined with media.</summary>
    public XPostPoll? Poll { get; init; }

    /// <summary>Makes this Post a reply.</summary>
    public XPostReply? Reply { get; init; }

    /// <summary>The ID of a Post to quote. X documents quote Posting as requiring an Enterprise plan.</summary>
    public string? QuoteTweetId { get; init; }

    /// <summary>Who may reply: see <see cref="XReplySettings"/>.</summary>
    public string? ReplySettings { get; init; }

    /// <summary>Attaches a place by ID.</summary>
    public XPostGeoRequest? Geo { get; init; }

    /// <summary>Edits an existing Post instead of creating a new one.</summary>
    public XPostEditOptions? EditOptions { get; init; }

    /// <summary>Shares a clip of another Post's video.</summary>
    public XPostClip? Clip { get; init; }

    /// <summary>Posts into a Community.</summary>
    public string? CommunityId { get; init; }

    /// <summary>Restricts the Post to super followers.</summary>
    public bool? ForSuperFollowersOnly { get; init; }

    /// <summary>Creates a promoted-only ("nullcast") Post.</summary>
    public bool? Nullcast { get; init; }

    /// <summary>Marks the Post as a paid partnership.</summary>
    public bool? PaidPartnership { get; init; }

    /// <summary>Marks the Post as made with AI.</summary>
    public bool? MadeWithAi { get; init; }

    /// <summary>Whether to share with followers (for Community Posts).</summary>
    public bool? ShareWithFollowers { get; init; }

    /// <summary>A Direct Message deep link.</summary>
    public string? DirectMessageDeepLink { get; init; }

    /// <summary>A card URI.</summary>
    public string? CardUri { get; init; }
}

/// <summary>Media attached to a Post.</summary>
public sealed record XPostMedia
{
    /// <summary>IDs returned by a media upload. Required.</summary>
    public required IReadOnlyList<string> MediaIds { get; init; }

    /// <summary>Up to 10 user IDs to tag in the media.</summary>
    public IReadOnlyList<string>? TaggedUserIds { get; init; }

    /// <summary>A title for the media (video).</summary>
    public string? Title { get; init; }

    /// <summary>A description for the media (video).</summary>
    public string? Description { get; init; }

    /// <summary>Whether the media may be embedded elsewhere.</summary>
    public bool? Embeddable { get; init; }

    /// <summary>The media ID of an image to use as the video preview.</summary>
    public string? PreviewMediaId { get; init; }

    /// <summary>A call-to-action button on the media. Set at most one of its properties.</summary>
    public XPostCallToActions? CallToActions { get; init; }
}

/// <summary>A call-to-action attached to Post media. The API accepts one of the three variants.</summary>
public sealed record XPostCallToActions
{
    /// <summary>An app-install button.</summary>
    public XAppInstallCallToAction? AppInstall { get; init; }

    /// <summary>A "visit site" button.</summary>
    public XUrlCallToAction? VisitSite { get; init; }

    /// <summary>A "watch now" button.</summary>
    public XUrlCallToAction? WatchNow { get; init; }
}

/// <summary>App-install call-to-action identifiers.</summary>
public sealed record XAppInstallCallToAction
{
    /// <summary>Apple App Store iPhone app ID.</summary>
    public string? AppStoreId { get; init; }

    /// <summary>Apple App Store iPad app ID.</summary>
    public string? IpadAppStoreId { get; init; }

    /// <summary>Google Play Store app ID.</summary>
    public string? PlayStoreId { get; init; }
}

/// <summary>A call-to-action that links to an HTTPS URL.</summary>
public sealed record XUrlCallToAction
{
    /// <summary>The HTTPS URL.</summary>
    public required string Url { get; init; }
}

/// <summary>A poll attached to a Post.</summary>
public sealed record XPostPoll
{
    /// <summary>Two to four options of 1 to 25 characters each.</summary>
    public required IReadOnlyList<string> Options { get; init; }

    /// <summary>Duration, 5 to 10080 minutes.</summary>
    public required int DurationMinutes { get; init; }

    /// <summary>Who may reply to the poll: see <see cref="XReplySettings"/>.</summary>
    public string? ReplySettings { get; init; }
}

/// <summary>Reply targeting.</summary>
public sealed record XPostReply
{
    /// <summary>The Post being replied to.</summary>
    public required string InReplyToTweetId { get; init; }

    /// <summary>User IDs to exclude from the reply's mentions.</summary>
    public IReadOnlyList<string>? ExcludeReplyUserIds { get; init; }
}

/// <summary>A place attachment.</summary>
public sealed record XPostGeoRequest
{
    /// <summary>The place ID.</summary>
    public required string PlaceId { get; init; }
}

/// <summary>Identifies the Post to edit.</summary>
public sealed record XPostEditOptions
{
    /// <summary>The most recent Post ID in the edit chain.</summary>
    public required string PreviousPostId { get; init; }
}

/// <summary>A video clip reference.</summary>
public sealed record XPostClip
{
    /// <summary>The source Post.</summary>
    public required string PostId { get; init; }

    /// <summary>Clip start in seconds.</summary>
    public required int StartSeconds { get; init; }

    /// <summary>Clip end in seconds.</summary>
    public required int EndSeconds { get; init; }
}

/// <summary>Allowed values for <c>reply_settings</c>.</summary>
public static class XReplySettings
{
    /// <summary>Only people the author follows.</summary>
    public const string Following = "following";

    /// <summary>Only mentioned users.</summary>
    public const string MentionedUsers = "mentionedUsers";

    /// <summary>Only subscribers.</summary>
    public const string Subscribers = "subscribers";

    /// <summary>Only verified accounts.</summary>
    public const string Verified = "verified";
}

/// <summary>The outcome of <c>CreateThreadAsync</c>. Threads are created one Post at a time and are never retried automatically.</summary>
public sealed record XThreadResult
{
    /// <summary>The Posts that were created, in order.</summary>
    public required IReadOnlyList<XCreatedPost> Posts { get; init; }

    /// <summary>The error that stopped the thread, or <see langword="null"/> when every Post was created.</summary>
    public XApiError? Error { get; init; }

    /// <summary>The zero-based index of the Post that failed.</summary>
    public int? FailedIndex { get; init; }

    /// <summary>True when every Post was created.</summary>
    public bool IsComplete => Error is null;
}
