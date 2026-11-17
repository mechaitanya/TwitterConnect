using System.Text.Json;

namespace XConnect.Models;

/// <summary>A bookmark folder.</summary>
public sealed record XBookmarkFolder
{
    /// <summary>Folder ID.</summary>
    public string? Id { get; init; }

    /// <summary>Folder name.</summary>
    public string? Name { get; init; }
}

/// <summary>Result of pinning or unpinning a List.</summary>
public sealed record XPinResult
{
    /// <summary>True while the List is pinned.</summary>
    public bool Pinned { get; init; }
}

/// <summary>Result of blocking or unblocking a user's Direct Messages.</summary>
public sealed record XDmBlockResult
{
    /// <summary>True while the user's Direct Messages are blocked.</summary>
    public bool Blocked { get; init; }
}

/// <summary>A trend personalised for the authenticated user.</summary>
public sealed record XPersonalizedTrend
{
    /// <summary>Category.</summary>
    public string? Category { get; init; }

    /// <summary>Post volume, as reported by X (a string).</summary>
    public string? PostCount { get; init; }

    /// <summary>Trend name.</summary>
    public string? TrendName { get; init; }

    /// <summary>How long the topic has been trending, as reported by X.</summary>
    public string? TrendingSince { get; init; }
}

/// <summary>A news story (<c>GET /2/news/{id}</c>).</summary>
public sealed record XNewsStory
{
    /// <summary>Story ID.</summary>
    public string? Id { get; init; }

    /// <summary>Headline.</summary>
    public string? Name { get; init; }

    /// <summary>Short hook.</summary>
    public string? Hook { get; init; }

    /// <summary>Summary.</summary>
    public string? Summary { get; init; }

    /// <summary>Category.</summary>
    public string? Category { get; init; }

    /// <summary>Disclaimer shown with generated content, if any.</summary>
    public string? Disclaimer { get; init; }

    /// <summary>When the story was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Posts that belong to the story.</summary>
    public IReadOnlyList<XNewsClusterPost>? ClusterPostsResults { get; init; }

    /// <summary>Contexts, as returned by X (the schema is an untyped object).</summary>
    public JsonElement? Contexts { get; init; }

    /// <summary>Keywords, as returned by X (the schema is an untyped object).</summary>
    public JsonElement? Keywords { get; init; }
}

/// <summary>A Post in a news cluster.</summary>
public sealed record XNewsClusterPost
{
    /// <summary>The Post ID.</summary>
    public string? PostId { get; init; }
}

/// <summary>Post-cap usage for the project (<c>GET /2/usage/tweets</c>).</summary>
public sealed record XPostUsage
{
    /// <summary>Project ID.</summary>
    public string? ProjectId { get; init; }

    /// <summary>The monthly Post cap, as reported by X (a string).</summary>
    public string? ProjectCap { get; init; }

    /// <summary>Usage so far in the period, as reported by X (a string).</summary>
    public string? ProjectUsage { get; init; }

    /// <summary>The day of the month the cap resets.</summary>
    public int? CapResetDay { get; init; }

    /// <summary>Daily usage per client app, as returned by X.</summary>
    public JsonElement? DailyClientAppUsage { get; init; }

    /// <summary>Daily usage of the project, as returned by X.</summary>
    public JsonElement? DailyProjectUsage { get; init; }
}

/// <summary>Credit balances (<c>GET /2/usage/credits</c>).</summary>
public sealed record XCreditBalance
{
    /// <summary>Total balance.</summary>
    public double TotalBalance { get; init; }

    /// <summary>Prepaid balance.</summary>
    public double PrepaidBalance { get; init; }

    /// <summary>Free balance.</summary>
    public double FreeBalance { get; init; }

    /// <summary>Free grants with their amounts and expiry, as returned by X.</summary>
    public JsonElement? FreeGrants { get; init; }
}

/// <summary>Filtered-stream rule counts (<c>GET /2/tweets/search/stream/rules/counts</c>).</summary>
public sealed record XRuleCounts
{
    /// <summary>The rule cap per client app, as reported by X (a string).</summary>
    public string? CapPerClientApp { get; init; }

    /// <summary>The rule cap per project, as reported by X (a string).</summary>
    public string? CapPerProject { get; init; }

    /// <summary>Rules used by the project, as reported by X (a string).</summary>
    public string? ProjectRulesCount { get; init; }

    /// <summary>Rules used by this client app, as returned by X.</summary>
    public JsonElement? ClientAppRulesCount { get; init; }

    /// <summary>Rules per client app, as returned by X.</summary>
    public JsonElement? AllProjectClientApps { get; init; }
}

/// <summary>The partitioned (Enterprise) Post streams. Each needs a <c>partition</c> number and an access level that includes it.</summary>
public enum XPostStreamKind
{
    /// <summary>A 10 percent sample (<c>/2/tweets/sample10/stream</c>).</summary>
    Sample10,

    /// <summary>The full firehose (<c>/2/tweets/firehose/stream</c>).</summary>
    Firehose,

    /// <summary>English-language firehose.</summary>
    FirehoseEnglish,

    /// <summary>Japanese-language firehose.</summary>
    FirehoseJapanese,

    /// <summary>Korean-language firehose.</summary>
    FirehoseKorean,

    /// <summary>Portuguese-language firehose.</summary>
    FirehosePortuguese,
}
