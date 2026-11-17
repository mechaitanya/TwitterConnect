using System.Text.Json.Serialization;

namespace XConnect.Models;

/// <summary>A user. Only the fields requested through <c>user.fields</c> (plus <c>id</c>, <c>name</c> and <c>username</c>) are populated.</summary>
public sealed record XUser
{
    /// <summary>User ID.</summary>
    public string? Id { get; init; }

    /// <summary>Display name.</summary>
    public string? Name { get; init; }

    /// <summary>The @handle without the at sign.</summary>
    public string? Username { get; init; }

    /// <summary>Account creation time.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Profile bio.</summary>
    public string? Description { get; init; }

    /// <summary>Profile location.</summary>
    public string? Location { get; init; }

    /// <summary>Profile URL.</summary>
    public string? Url { get; init; }

    /// <summary>Avatar URL.</summary>
    public string? ProfileImageUrl { get; init; }

    /// <summary>Banner URL.</summary>
    public string? ProfileBannerUrl { get; init; }

    /// <summary>True when the account is protected.</summary>
    public bool? Protected { get; init; }

    /// <summary>True when the account is verified.</summary>
    public bool? Verified { get; init; }

    /// <summary>Verification type.</summary>
    public string? VerifiedType { get; init; }

    /// <summary>ID of the pinned Post (requires the <c>pinned_post_id</c> expansion).</summary>
    public string? PinnedPostId { get; init; }

    /// <summary>ID of the most recent Post.</summary>
    public string? MostRecentPostId { get; init; }

    /// <summary>Public counts such as followers.</summary>
    public XUserPublicMetrics? PublicMetrics { get; init; }

    /// <summary>Withholding details.</summary>
    public XWithheld? Withheld { get; init; }

    /// <summary>Followers who were recently active.</summary>
    public long? ActiveFollowerCount { get; init; }

    /// <summary>The affiliation badge of an organisation member.</summary>
    public XUserAffiliation? Affiliation { get; init; }

    /// <summary>The confirmed email address (only for the authenticated user, with the right scope).</summary>
    public string? ConfirmedEmail { get; init; }

    /// <summary>How the authenticated user relates to this user, for example <c>following</c> or <c>muting</c>.</summary>
    public IReadOnlyList<string>? ConnectionStatus { get; init; }

    /// <summary>Entities in the description and URL.</summary>
    public XUserEntities? Entities { get; init; }

    /// <summary>True when identity verification is complete.</summary>
    public bool? IsIdentityVerified { get; init; }

    /// <summary>True for organisation-only accounts.</summary>
    public bool? OrgOnly { get; init; }

    /// <summary>True when the account is marked as parody.</summary>
    public bool? Parody { get; init; }

    /// <summary>Whether the user receives your X Chat messages, as returned.</summary>
    public string? ReceivesYourChat { get; init; }

    /// <summary>Whether the user receives your Direct Messages.</summary>
    public bool? ReceivesYourDm { get; init; }

    /// <summary>The number of subscribers.</summary>
    public long? SubscriberCount { get; init; }

    /// <summary>True when the user subscribes to the authenticated user.</summary>
    public bool? SubscribesToYou { get; init; }

    /// <summary>Subscription details.</summary>
    public XUserSubscription? Subscription { get; init; }

    /// <summary>The subscription type, as returned.</summary>
    public string? SubscriptionType { get; init; }

    /// <summary>The number of verified followers.</summary>
    public long? VerifiedFollowersCount { get; init; }
}

/// <summary>Public user metrics.</summary>
public sealed record XUserPublicMetrics
{
    /// <summary>Followers.</summary>
    public long? FollowersCount { get; init; }

    /// <summary>Accounts followed.</summary>
    public long? FollowingCount { get; init; }

    /// <summary>Posts, including reposts.</summary>
    public long? PostCount { get; init; }

    /// <summary>Lists that include the user.</summary>
    public long? ListedCount { get; init; }

    /// <summary>Posts liked.</summary>
    public long? LikeCount { get; init; }

    /// <summary>Media posted.</summary>
    public long? MediaCount { get; init; }
}

/// <summary>Result of following a user.</summary>
public sealed record XFollowResult
{
    /// <summary>True when the follow took effect.</summary>
    public bool Following { get; init; }

    /// <summary>True when the target is protected and a request is pending.</summary>
    public bool PendingFollow { get; init; }
}

/// <summary>Result of unfollowing a user.</summary>
public sealed record XUnfollowResult
{
    /// <summary>True while still following.</summary>
    public bool Following { get; init; }
}

/// <summary>Result of blocking or unblocking a user.</summary>
public sealed record XBlockResult
{
    /// <summary>True while the user is blocked.</summary>
    public bool Blocking { get; init; }
}

/// <summary>Result of muting or unmuting a user.</summary>
public sealed record XMuteResult
{
    /// <summary>True while the user is muted.</summary>
    public bool Muting { get; init; }
}

/// <summary>Result of liking or unliking a Post.</summary>
public sealed record XLikeResult
{
    /// <summary>True while the Post is liked.</summary>
    public bool Liked { get; init; }
}

/// <summary>Result of reposting or undoing a repost.</summary>
public sealed record XRepostResult
{
    /// <summary>True while the Post is reposted.</summary>
    public bool Retweeted { get; init; }

    /// <summary>The ID of the repost (only returned when creating one).</summary>
    public string? RestId { get; init; }
}

/// <summary>Result of bookmarking or removing a bookmark.</summary>
public sealed record XBookmarkResult
{
    /// <summary>True while the Post is bookmarked.</summary>
    public bool Bookmarked { get; init; }

    /// <summary>The ID of the Post, when the API echoes it (it is called <c>tweet_id</c> on the wire).</summary>
    [JsonPropertyName("tweet_id")]
    public string? PostId { get; init; }
}

/// <summary>An organisation affiliation badge.</summary>
public sealed record XUserAffiliation
{
    /// <summary>The badge image URL.</summary>
    public string? BadgeUrl { get; init; }

    /// <summary>The description.</summary>
    public string? Description { get; init; }

    /// <summary>The URL.</summary>
    public string? Url { get; init; }

    /// <summary>The affiliated organisation's user IDs.</summary>
    public IReadOnlyList<string>? UserId { get; init; }
}

/// <summary>Entities in a profile.</summary>
public sealed record XUserEntities
{
    /// <summary>Entities in the description.</summary>
    public XPostEntities? Description { get; init; }

    /// <summary>Entities in the profile URL.</summary>
    public XUserUrlEntities? Url { get; init; }
}

/// <summary>URL entities of a profile URL.</summary>
public sealed record XUserUrlEntities
{
    /// <summary>The URLs.</summary>
    public IReadOnlyList<XUrlEntity>? Urls { get; init; }
}

/// <summary>Whether a user subscribes to the authenticated user.</summary>
public sealed record XUserSubscription
{
    /// <summary>True when subscribed.</summary>
    public bool? SubscribesToYou { get; init; }
}
