using System.Net;

namespace XConnect;

/// <summary>The outcome of a request to create a Post.</summary>
/// <param name="StatusCode">HTTP status code returned by the X API.</param>
/// <param name="PostId">The ID of the created Post, when successful.</param>
/// <param name="Text">The text of the created Post, when successful.</param>
/// <param name="Error">Error detail from the X API, when the request failed.</param>
/// <param name="RateLimit">Rate limit information reported by the X API, if present.</param>
public sealed record XPostResult(
    HttpStatusCode StatusCode,
    string? PostId,
    string? Text,
    string? Error,
    XRateLimit? RateLimit)
{
    /// <summary>True when X created the Post (HTTP 201).</summary>
    public bool IsSuccess => StatusCode == HttpStatusCode.Created && PostId is not null;
}

/// <summary>Rate limit state from the <c>x-rate-limit-*</c> response headers.</summary>
/// <param name="Limit">Maximum requests allowed in the window.</param>
/// <param name="Remaining">Requests remaining in the window.</param>
/// <param name="ResetsAt">When the window resets.</param>
public sealed record XRateLimit(int Limit, int Remaining, DateTimeOffset ResetsAt);
