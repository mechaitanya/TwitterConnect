using System.Net;
using System.Text;
using XConnect.Authentication;

namespace XConnect.Tests;

/// <summary>A captured request: the body is read eagerly because content is disposed after sending.</summary>
internal sealed record CapturedRequest(HttpRequestMessage Message, string? Body, byte[]? BodyBytes)
{
    public HttpMethod Method => Message.Method;

    public Uri Uri => Message.RequestUri!;

    public string PathAndQuery => Uri.PathAndQuery;

    public string? Authorization => Message.Headers.Authorization?.ToString();
}

/// <summary>Replays queued responses (or exceptions) and records every request.</summary>
internal sealed class QueueHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _queue = new();
    private readonly Func<HttpRequestMessage, HttpResponseMessage>? _fallback;

    public QueueHandler(Func<HttpRequestMessage, HttpResponseMessage>? fallback = null) => _fallback = fallback;

    public List<CapturedRequest> Requests { get; } = [];

    /// <summary>
    /// Every request the SDK sends through a handler is checked against the official OpenAPI contract (query parameters, types, enums, ranges,
    /// request body shape and required fields). A violation throws, so the calling test fails. Turn off only for tests that deliberately send
    /// something the contract forbids.
    /// </summary>
    public bool Audit { get; init; } = true;

    private static void AuditAgainstSpecification(HttpRequestMessage request, CapturedRequest captured)
    {
        var uri = request.RequestUri!;
        if (SpecContract.Instance.Match(request.Method.Method, uri.AbsolutePath) is not { } operation)
        {
            return;
        }

        var mediaType = request.Content?.Headers.ContentType?.MediaType;
        var problems = SpecContract.Instance.ValidateRequest(operation, uri.Query, mediaType, captured.Body);
        if (problems.Count > 0)
        {
            throw new SpecViolationException($"{operation.Key} violates the OpenAPI contract:{Environment.NewLine}  - {string.Join(Environment.NewLine + "  - ", problems)}");
        }
    }

    public QueueHandler Enqueue(HttpResponseMessage response)
    {
        _queue.Enqueue(_ => response);
        return this;
    }

    public QueueHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _queue.Enqueue(respond);
        return this;
    }

    public QueueHandler Enqueue(HttpStatusCode status, string json, Action<HttpResponseMessage>? configure = null)
    {
        _queue.Enqueue(_ =>
        {
            var response = Json(status, json);
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    public QueueHandler EnqueueThrow(Exception exception)
    {
        _queue.Enqueue(_ => throw exception);
        return this;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? bytes = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var isText = request.Content?.Headers.ContentType?.MediaType is "application/json" or "application/x-www-form-urlencoded";
        var captured = new CapturedRequest(request, isText && bytes is not null ? Encoding.UTF8.GetString(bytes) : null, bytes);
        lock (Requests)
        {
            Requests.Add(captured);
        }

        if (Audit)
        {
            AuditAgainstSpecification(request, captured);
        }

        if (_queue.Count > 0)
        {
            return _queue.Dequeue()(request);
        }

        return _fallback?.Invoke(request) ?? throw new InvalidOperationException($"No response queued for {request.Method} {request.RequestUri}.");
    }
}

/// <summary>A clock that never waits: timers fire immediately and the requested delays are recorded.</summary>
internal sealed class InstantTimeProvider(DateTimeOffset? now = null) : TimeProvider
{
    private readonly DateTimeOffset _now = now ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public List<TimeSpan> Delays { get; } = [];

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (Delays)
            {
                Delays.Add(dueTime);
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return new Timer0();
    }

    private sealed class Timer0 : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal static class TestClients
{
    public static readonly XCredentials Credentials = new("key", "secret", "token", "token-secret");

    public static XConnectOptions OAuth1Options() => new XConnectOptions().UseOAuth1(Credentials);

    public static XConnectClient Create(QueueHandler handler, XConnectOptions? options = null, TimeProvider? time = null)
    {
        options ??= OAuth1Options();
        return new XConnectClient(new HttpClient(handler), options, time ?? new InstantTimeProvider());
    }

    public static string Ok(string json) => json;
}
