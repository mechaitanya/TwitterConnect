using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XConnect.Authentication;
using XConnect.Models;
using XConnect.Serialization;

namespace XConnect.Infrastructure;

/// <summary>
/// The shared HTTP pipeline: authentication selection, serialization, error parsing, rate-limit tracking
/// and safe retries. Every API client goes through it.
/// </summary>
internal sealed partial class XApiTransport
{
    internal static readonly ActivitySource ActivitySource = new("XConnect");

    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;
    private readonly XConnectOptions _options;
    private readonly IReadOnlyList<IXAuthenticator> _authenticators;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim? _concurrency;

    public XApiTransport(
        HttpClient httpClient,
        XConnectOptions options,
        IReadOnlyList<IXAuthenticator> authenticators,
        XRateLimitTracker rateLimits,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _authenticators = authenticators;
        RateLimits = rateLimits;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _concurrency = options.MaxConcurrentRequests is { } max ? new SemaphoreSlim(max, max) : null;

        var baseAddress = options.BaseAddress.AbsoluteUri;
        _baseAddress = new Uri(baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/");
    }

    public XRateLimitTracker RateLimits { get; }

    public TimeProvider TimeProvider => _timeProvider;

    public XConnectOptions Options => _options;

    /// <summary>Sends a request and wraps the JSON envelope in an <see cref="XResponse{T}"/>.</summary>
    public async Task<XResponse<T>> SendAsync<T>(XApiRequest request, JsonTypeInfo<XEnvelope<T>> typeInfo, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("XConnect " + request.Method.Method + " " + request.Route);
        activity?.SetTag("http.request.method", request.Method.Method);
        activity?.SetTag("url.template", request.Route);

        var (response, body) = await SendWithRetriesAsync(request, cancellationToken).ConfigureAwait(false);
        using (response)
        {
            activity?.SetTag("http.response.status_code", (int)response.StatusCode);
            return Parse(request, response, body, typeInfo);
        }
    }

    /// <summary>
    /// Opens a streaming response. The headers are read but the body is not buffered. No retries happen here;
    /// the caller owns reconnection.
    /// </summary>
    public async Task<HttpResponseMessage> OpenStreamAsync(XApiRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = await BuildAsync(request, cancellationToken).ConfigureAwait(false);
        return await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a request exactly as built, without adding credentials. Used for pre-signed URLs, which carry their own authorisation.</summary>
    public Task<HttpResponseMessage> SendUnauthenticatedAsync(HttpRequestMessage request, HttpCompletionOption completion, CancellationToken cancellationToken) =>
        _httpClient.SendAsync(request, completion, cancellationToken);

    /// <summary>Converts a failed response into an <see cref="XApiError"/>.</summary>
    public static XApiError ToError(HttpResponseMessage response, string body) => XErrorParser.Parse(response.StatusCode, body, GetRequestId(response));

    public XRateLimit? ReadAndRecordRateLimit(XApiRequest request, HttpResponseMessage response)
    {
        var limit = XErrorParser.ParseRateLimit(response);
        if (limit is not null)
        {
            RateLimits.Record(request.Method, request.Route, limit);
        }

        return limit;
    }

    private async Task<(HttpResponseMessage Response, string Body)> SendWithRetriesAsync(XApiRequest request, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage? response = null;
            try
            {
                using var httpRequest = await BuildAsync(request, cancellationToken).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (_options.Timeout != Timeout.InfiniteTimeSpan)
                {
                    timeout.CancelAfter(_options.Timeout);
                }

                string body;
                if (_concurrency is not null)
                {
                    await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    try
                    {
                        response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException($"The request to {request.Route} timed out after {_options.Timeout}.", ex);
                    }

                    body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _concurrency?.Release();
                }

                ReadAndRecordRateLimit(request, response);

                var delay = attempt < _options.Retry.MaxRetries
                    ? XRetryPolicy.GetRetryDelay(request.Method, response, attempt, _options.Retry, _timeProvider.GetUtcNow())
                    : null;

                if (delay is null)
                {
                    return (response, body);
                }

                LogRetry(request.Method.Method, request.Route, (int)response.StatusCode, attempt + 1, delay.Value);
                response.Dispose();
                response = null;
                await Task.Delay(delay.Value, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TimeoutException
                && attempt < _options.Retry.MaxRetries
                && XRetryPolicy.IsIdempotent(request.Method)
                && !cancellationToken.IsCancellationRequested)
            {
                // Network failures are only retried for idempotent methods. A POST that fails ambiguously is never retried:
                // X may have processed it even though the response was lost.
                response?.Dispose();
                var delay = XRetryPolicy.GetBackoff(attempt, _options.Retry);
                LogNetworkRetry(request.Method.Method, request.Route, attempt + 1, ex);
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                response?.Dispose();
                throw;
            }

            attempt++;
        }
    }

    private async Task<HttpRequestMessage> BuildAsync(XApiRequest request, CancellationToken cancellationToken)
    {
        var authenticator = Select(request);
        var httpRequest = new HttpRequestMessage(request.Method, request.BuildUri(_baseAddress));
        try
        {
            if (request.Content is { } content)
            {
                httpRequest.Content = content();
            }

            await authenticator.AuthenticateAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            return httpRequest;
        }
        catch
        {
            httpRequest.Dispose();
            throw;
        }
    }

    private IXAuthenticator Select(XApiRequest request)
    {
        foreach (var authenticator in _authenticators)
        {
            if ((authenticator.Method & request.Auth) != 0)
            {
                return authenticator;
            }
        }

        throw new InvalidOperationException(
            $"{request.Method.Method} {request.Route} accepts {request.Auth} authentication, but only {Describe(_authenticators)} is configured.");

        static string Describe(IReadOnlyList<IXAuthenticator> authenticators) =>
            authenticators.Count == 0 ? "no authentication" : string.Join(", ", authenticators.Select(a => a.Method));
    }

    private XResponse<T> Parse<T>(XApiRequest request, HttpResponseMessage response, string body, JsonTypeInfo<XEnvelope<T>> typeInfo)
    {
        var rateLimit = XErrorParser.ParseRateLimit(response);
        var requestId = GetRequestId(response);

        if (!response.IsSuccessStatusCode)
        {
            return new XResponse<T>(response.StatusCode, default, null, null, [], XErrorParser.Parse(response.StatusCode, body, requestId), rateLimit, requestId);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new XResponse<T>(response.StatusCode, default, null, null, [], null, rateLimit, requestId);
        }

        try
        {
            var envelope = JsonSerializer.Deserialize(body, typeInfo);
            var partial = envelope?.Errors?.Select(p => XErrorParser.FromProblem(p, response.StatusCode, requestId)).ToArray() ?? [];
            return new XResponse<T>(response.StatusCode, envelope is null ? default : envelope.Data, envelope?.Meta, envelope?.Includes, partial, null, rateLimit, requestId);
        }
        catch (JsonException ex)
        {
            LogMalformed(request.Method.Method, request.Route, (int)response.StatusCode);
            var error = new XApiError(XApiErrorKind.Serialization, response.StatusCode, null, "Malformed response", ex.Message, null, requestId);
            return new XResponse<T>(response.StatusCode, default, null, null, [], error, rateLimit, requestId);
        }
    }

    internal static string? GetRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-transaction-id", out var values) ? values.FirstOrDefault() : null;

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Retrying {Method} {Route} after HTTP {Status} (retry {Attempt}, waiting {Delay})")]
    private partial void LogRetry(string method, string route, int status, int attempt, TimeSpan delay);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Retrying idempotent {Method} {Route} after a transient failure (retry {Attempt})")]
    private partial void LogNetworkRetry(string method, string route, int attempt, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "{Method} {Route} returned HTTP {Status} with a body that could not be parsed")]
    private partial void LogMalformed(string method, string route, int status);
}

/// <summary>Decides when and for how long a failed request may be retried.</summary>
internal static class XRetryPolicy
{
    /// <summary>HTTP semantics (RFC 9110) mark these methods idempotent, so repeating them is safe.</summary>
    public static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Put
        || method == HttpMethod.Delete || method == HttpMethod.Options;

    /// <summary>
    /// Returns how long to wait before retrying, or <see langword="null"/> when the response must not be retried.
    /// HTTP 429 means X rejected the request before processing it, so it is retried for every method.
    /// 5xx responses are ambiguous and are retried only for idempotent methods.
    /// </summary>
    public static TimeSpan? GetRetryDelay(HttpMethod method, HttpResponseMessage response, int attempt, XRetryOptions options, DateTimeOffset now)
    {
        var status = (int)response.StatusCode;
        var retryable = status == 429 || (status is 500 or 502 or 503 or 504 && IsIdempotent(method));
        if (!retryable)
        {
            return null;
        }

        var hinted = RetryAfter(response, now);
        if (hinted is null && status == 429 && XErrorParser.ParseRateLimit(response) is { } limit)
        {
            // One second of margin: the reset instant can equal "now", or the local clock can run ahead of X.
            hinted = ((limit.ResetsAt - now) > TimeSpan.Zero ? limit.ResetsAt - now : TimeSpan.Zero) + TimeSpan.FromSeconds(1);
        }

        var delay = hinted ?? GetBackoff(attempt, options);
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        // Never wait longer than configured: hand the response back so the caller can decide.
        return delay > options.MaxDelay ? null : delay;
    }

    public static TimeSpan GetBackoff(int attempt, XRetryOptions options)
    {
        var factor = Math.Pow(2, Math.Min(attempt, 16));
        var delay = TimeSpan.FromTicks((long)(options.BaseDelay.Ticks * factor));
        delay = delay > options.MaxDelay ? options.MaxDelay : delay;
        return ApplyJitter(delay, options.Jitter, options.RandomSource);
    }

    /// <summary>Scales a delay by a random factor in [1 - jitter, 1]; the result never exceeds the input, so caps stay caps.</summary>
    internal static TimeSpan ApplyJitter(TimeSpan delay, double jitter, Func<double>? random = null)
    {
        jitter = Math.Clamp(jitter, 0, 1);
        if (jitter == 0 || delay <= TimeSpan.Zero)
        {
            return delay;
        }

        var r = Math.Clamp((random ?? Random.Shared.NextDouble)(), 0, 1);
        return TimeSpan.FromTicks((long)(delay.Ticks * (1 - (jitter * r))));
    }

    /// <summary>Lengthens a delay X asked for by up to <paramref name="jitter"/> of itself: never shorter than requested.</summary>
    internal static TimeSpan ExtendWithJitter(TimeSpan floor, double jitter, Func<double>? random = null)
    {
        jitter = Math.Clamp(jitter, 0, 1);
        var r = Math.Clamp((random ?? Random.Shared.NextDouble)(), 0, 1);
        return jitter == 0 ? floor : TimeSpan.FromTicks(floor.Ticks + (long)(floor.Ticks * jitter * r));
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - now : null;
    }
}

/// <summary>Turns error bodies and rate-limit headers into typed values.</summary>
internal static class XErrorParser
{
    public static XApiError Parse(HttpStatusCode status, string body, string? requestId)
    {
        XProblem? problem = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                problem = JsonSerializer.Deserialize(body, XConnectJsonContext.Default.XProblem);
            }
            catch (JsonException)
            {
                // Not JSON (for example an HTML error page from a proxy): fall through and use the raw text.
            }
        }

        if (problem is not null && problem.Title is null && problem.Detail is null && problem.Message is null && problem.Errors is { Count: > 0 } nested)
        {
            problem = nested[0];
        }

        if (problem is null || (problem.Title is null && problem.Detail is null && problem.Message is null))
        {
            var text = string.IsNullOrWhiteSpace(body) ? null : body.Length > 500 ? body[..500] : body;
            return new XApiError(Classify(status, null), status, null, null, text, null, requestId);
        }

        return FromProblem(problem, status, requestId);
    }

    public static XApiError FromProblem(XProblem problem, HttpStatusCode status, string? requestId)
    {
        var detail = problem.Detail ?? problem.Message;
        var effective = problem.Status is { } s ? (HttpStatusCode)s : status;
        return new XApiError(Classify(status, detail, problem.Type), effective == 0 ? status : effective, problem.Type, problem.Title, detail, problem.Code, requestId);
    }

    /// <summary>
    /// Classifies a failure. X documents the problem <c>type</c> values (<c>invalid-request</c>, <c>resource-not-found</c>,
    /// <c>not-authorized-for-resource</c>, <c>client-forbidden</c>, <c>usage-capped</c>, <c>rate-limit-exceeded</c>,
    /// <c>streaming-connection</c>, <c>rule-cap</c>, <c>invalid-rules</c>, <c>duplicate-rules</c>); the type is consulted first
    /// and the HTTP status is the fallback.
    /// </summary>
    public static XApiErrorKind Classify(HttpStatusCode status, string? detail, string? type = null)
    {
        var slug = type is null ? null : type[(type.LastIndexOf('/') + 1)..];
        switch (slug)
        {
            case "rate-limit-exceeded" or "usage-capped":
                return XApiErrorKind.RateLimited;
            case "resource-not-found":
                return XApiErrorKind.NotFound;
            case "invalid-request" or "invalid-rules" or "duplicate-rules" or "rule-cap":
                return XApiErrorKind.Validation;
            case "streaming-connection":
                return XApiErrorKind.Conflict;
            case "not-authorized-for-resource" or "client-forbidden":
                return XApiErrorKind.Authorization;
        }

        var code = (int)status;
        return code switch
        {
            401 => XApiErrorKind.Authentication,
            403 when detail?.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true => XApiErrorKind.DuplicatePost,
            403 => XApiErrorKind.Authorization,
            404 => XApiErrorKind.NotFound,
            409 => XApiErrorKind.Conflict,
            429 => XApiErrorKind.RateLimited,
            400 or 422 => XApiErrorKind.Validation,
            >= 500 => XApiErrorKind.Server,
            _ => XApiErrorKind.Unknown,
        };
    }

    public static XRateLimit? ParseRateLimit(HttpResponseMessage response)
    {
        if (TryGet(response, "x-rate-limit-limit", out var limit)
            && TryGet(response, "x-rate-limit-remaining", out var remaining)
            && TryGet(response, "x-rate-limit-reset", out var reset))
        {
            try
            {
                return new XRateLimit(checked((int)limit), checked((int)remaining), DateTimeOffset.FromUnixTimeSeconds(reset));
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
            {
                return null;
            }
        }

        return null;
    }

    private static bool TryGet(HttpResponseMessage response, string name, out long value)
    {
        value = 0;
        return response.Headers.TryGetValues(name, out var values)
            && long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
