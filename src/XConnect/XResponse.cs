using System.Net;

namespace XConnect;

/// <summary>The typed outcome of an X API call. HTTP failures are reported here rather than thrown.</summary>
/// <typeparam name="T">The type of the <c>data</c> payload.</typeparam>
public sealed class XResponse<T>
{
    internal XResponse(
        HttpStatusCode statusCode,
        T? data,
        Models.XMeta? meta,
        Models.XIncludes? includes,
        IReadOnlyList<XApiError> partialErrors,
        XApiError? error,
        XRateLimit? rateLimit,
        string? requestId)
    {
        StatusCode = statusCode;
        Data = data;
        Meta = meta;
        Includes = includes;
        PartialErrors = partialErrors;
        Error = error;
        RateLimit = rateLimit;
        RequestId = requestId;
    }

    /// <summary>The HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The <c>data</c> payload; <see langword="null"/> when the call failed or X returned none.</summary>
    public T? Data { get; }

    /// <summary>Pagination and result metadata, when present.</summary>
    public Models.XMeta? Meta { get; }

    /// <summary>Expanded objects requested through <c>expansions</c>, when present.</summary>
    public Models.XIncludes? Includes { get; }

    /// <summary>Per-item problems X reported alongside a successful response (for example an ID that was not found).</summary>
    public IReadOnlyList<XApiError> PartialErrors { get; }

    /// <summary>The failure, or <see langword="null"/> when the call succeeded.</summary>
    public XApiError? Error { get; }

    /// <summary>Rate limit state for the endpoint, when X reported it.</summary>
    public XRateLimit? RateLimit { get; }

    /// <summary>The X transaction identifier, if the response carried one.</summary>
    public string? RequestId { get; }

    internal XResponse<TOut> Map<TOut>(Func<T, TOut> map) =>
        new(StatusCode, Data is null ? default : map(Data), Meta, Includes, PartialErrors, Error, RateLimit, RequestId);

    /// <summary>True when X accepted the request.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>The token that retrieves the next page, or <see langword="null"/> when there are no more pages.</summary>
    public string? NextToken => Meta?.NextToken;

    /// <summary>Returns <see cref="Data"/>, or throws <see cref="XApiException"/> if the call failed or returned no data.</summary>
    /// <exception cref="XApiException">The call failed or returned no data.</exception>
    public T GetDataOrThrow()
    {
        if (Error is not null)
        {
            throw new XApiException(Error);
        }

        return Data ?? throw new XApiException(
            "X returned a successful response without data.",
            new XApiError(XApiErrorKind.Serialization, StatusCode, null, null, "The response contained no data.", null, RequestId));
    }
}
