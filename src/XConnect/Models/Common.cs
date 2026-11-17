namespace XConnect.Models;

/// <summary>Result and pagination metadata returned with list endpoints.</summary>
public sealed record XMeta
{
    /// <summary>Number of results in this page.</summary>
    public int? ResultCount { get; init; }

    /// <summary>Token for the next page; absent on the last page.</summary>
    public string? NextToken { get; init; }

    /// <summary>Token for the previous page.</summary>
    public string? PreviousToken { get; init; }

    /// <summary>ID of the newest item in the result set.</summary>
    public string? NewestId { get; init; }

    /// <summary>ID of the oldest item in the result set.</summary>
    public string? OldestId { get; init; }

    /// <summary>Total Posts matched by a counts query.</summary>
    public long? TotalPostCount { get; init; }

    /// <summary>Timestamp of a stream-rule update.</summary>
    public string? Sent { get; init; }

    /// <summary>Summary of a stream-rule update.</summary>
    public XRuleSummary? Summary { get; init; }
}

/// <summary>Objects expanded through the <c>expansions</c> parameter.</summary>
public sealed record XIncludes
{
    /// <summary>Referenced users.</summary>
    public IReadOnlyList<XUser>? Users { get; init; }

    /// <summary>Referenced Posts.</summary>
    public IReadOnlyList<XPost>? Posts { get; init; }

    /// <summary>Referenced media.</summary>
    public IReadOnlyList<XMedia>? Media { get; init; }

    /// <summary>Referenced polls.</summary>
    public IReadOnlyList<XPoll>? Polls { get; init; }

    /// <summary>Referenced places.</summary>
    public IReadOnlyList<XPlace>? Places { get; init; }
}

/// <summary>A problem entry as sent by X, either as a top-level error body or inside <c>errors</c>.</summary>
public sealed record XProblem
{
    /// <summary>Problem type URI.</summary>
    public string? Type { get; init; }

    /// <summary>Short title.</summary>
    public string? Title { get; init; }

    /// <summary>Detail text.</summary>
    public string? Detail { get; init; }

    /// <summary>HTTP status echoed in the body.</summary>
    public int? Status { get; init; }

    /// <summary>Numeric code (legacy error format).</summary>
    public int? Code { get; init; }

    /// <summary>Message (legacy error format).</summary>
    public string? Message { get; init; }

    /// <summary>The offending value, for per-item problems.</summary>
    public string? Value { get; init; }

    /// <summary>The ID of the unavailable resource.</summary>
    public string? ResourceId { get; init; }

    /// <summary>The kind of the unavailable resource.</summary>
    public string? ResourceType { get; init; }

    /// <summary>The offending parameter.</summary>
    public string? Parameter { get; init; }

    /// <summary>Problems X reports in a body-level <c>errors</c> array.</summary>
    public IReadOnlyList<XProblem>? Errors { get; init; }
}

/// <summary>Wire envelope shared by all X API v2 JSON responses.</summary>
internal sealed record XEnvelope<T>
{
    public T? Data { get; init; }

    public XMeta? Meta { get; init; }

    public XIncludes? Includes { get; init; }

    public IReadOnlyList<XProblem>? Errors { get; init; }
}

/// <summary>Pagination bounds for automatic traversal.</summary>
public sealed record XPaginationOptions
{
    /// <summary>The maximum number of pages to fetch. Defaults to 10 so traversal is always bounded.</summary>
    public int MaxPages { get; init; } = 10;

    /// <summary>The maximum number of items to yield, or <see langword="null"/> for no item limit.</summary>
    public int? MaxItems { get; init; }
}
