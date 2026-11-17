using System.Net;

namespace XConnect;

/// <summary>Broad classification of a failed X API call.</summary>
public enum XApiErrorKind
{
    /// <summary>The failure did not match a known category.</summary>
    Unknown = 0,

    /// <summary>The request was rejected as invalid (HTTP 400 or 422).</summary>
    Validation,

    /// <summary>The credentials were missing, invalid or expired (HTTP 401).</summary>
    Authentication,

    /// <summary>The credentials are valid but not permitted to perform the operation (HTTP 403).</summary>
    Authorization,

    /// <summary>A rate limit or usage cap was exceeded (HTTP 429).</summary>
    RateLimited,

    /// <summary>
    /// X rejected the Post as duplicate content. X does not document the error for duplicate Posts, so this is detected
    /// heuristically: HTTP 403 whose detail text mentions "duplicate". Treat it as a hint, not a contract.
    /// </summary>
    DuplicatePost,

    /// <summary>The resource does not exist or is not visible to the caller (HTTP 404).</summary>
    NotFound,

    /// <summary>X reported a server-side failure (HTTP 5xx).</summary>
    Server,

    /// <summary>The response body could not be parsed into the expected shape.</summary>
    Serialization,

    /// <summary>The request conflicts with current state (HTTP 409), for example opening a second connection to a single-connection stream.</summary>
    Conflict,
}

/// <summary>A structured error returned by the X API, or synthesised when a response could not be parsed.</summary>
/// <param name="Kind">The error category.</param>
/// <param name="StatusCode">The HTTP status code of the response.</param>
/// <param name="Type">The problem type URI reported by X, if any.</param>
/// <param name="Title">The short problem title, if any.</param>
/// <param name="Detail">The human-readable problem detail, if any.</param>
/// <param name="ErrorCode">The numeric X error code, if the response used that format.</param>
/// <param name="RequestId">
/// The value of the <c>x-transaction-id</c> response header, if present. X's documentation does not describe this header
/// (it documents only the <c>x-rate-limit-*</c> headers), so treat it as best-effort and do not depend on it.
/// </param>
public sealed record XApiError(
    XApiErrorKind Kind,
    HttpStatusCode StatusCode,
    string? Type,
    string? Title,
    string? Detail,
    int? ErrorCode,
    string? RequestId)
{
    /// <summary>The most descriptive message available for this error.</summary>
    public string Message => Detail ?? Title ?? $"X API request failed with HTTP {(int)StatusCode}.";

    /// <inheritdoc />
    public override string ToString() => $"{Kind} ({(int)StatusCode}): {Message}";
}

/// <summary>Thrown by APIs that do not return a typed result, and by <see cref="XResponse{T}.GetDataOrThrow"/>.</summary>
public sealed class XApiException : Exception
{
    /// <summary>Creates the exception from an API error.</summary>
    public XApiException(XApiError error)
        : base(error?.ToString())
    {
        Error = error!;
    }

    /// <summary>Creates the exception with a custom message.</summary>
    public XApiException(string message, XApiError error)
        : base(message)
    {
        Error = error;
    }

    /// <summary>The structured error.</summary>
    public XApiError Error { get; }
}

/// <summary>Thrown when credentials are missing or an OAuth 2.0 token cannot be obtained or refreshed.</summary>
public sealed class XAuthenticationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public XAuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner exception.</summary>
    public XAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
