using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using XConnect.Authentication;
using XConnect.Infrastructure;
using XConnect.Models;
using XConnect.Serialization;

namespace XConnect.Streaming;

/// <summary>One line of a Post stream.</summary>
public sealed record XStreamEvent
{
    /// <summary>The Post, when the line carried one.</summary>
    public XPost? Post { get; init; }

    /// <summary>Expanded objects.</summary>
    public XIncludes? Includes { get; init; }

    /// <summary>The filtered-stream rules that matched the Post.</summary>
    public IReadOnlyList<XMatchingRule>? MatchingRules { get; init; }

    /// <summary>Problems X reported in-band, for example a connection warning.</summary>
    public IReadOnlyList<XProblem>? Errors { get; init; }
}

/// <summary>A filtered-stream rule that matched.</summary>
public sealed record XMatchingRule
{
    /// <summary>Rule ID.</summary>
    public string? Id { get; init; }

    /// <summary>Rule tag.</summary>
    public string? Tag { get; init; }
}

/// <summary>The stream envelope as sent on the wire.</summary>
internal sealed record XStreamEnvelope
{
    public XPost? Data { get; init; }

    public XIncludes? Includes { get; init; }

    public IReadOnlyList<XMatchingRule>? MatchingRules { get; init; }

    public IReadOnlyList<XProblem>? Errors { get; init; }
}

/// <summary>Options for a streaming connection.</summary>
public sealed record XStreamOptions
{
    /// <summary>Fields and expansions for each streamed Post.</summary>
    public XFieldSelection? Fields { get; init; }

    /// <summary>Recover up to this many minutes (0 to 5) of Posts missed during a disconnect, where your access level allows it.</summary>
    public int? BackfillMinutes { get; init; }

    /// <summary>The maximum consecutive failed connection attempts before giving up. Defaults to 10.</summary>
    public int MaxReconnects { get; init; } = 10;

    /// <summary>The first reconnect delay, doubled after each failure. Defaults to 1 second.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest reconnect delay. Defaults to 5 minutes.</summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The minimum delay after HTTP 429. X asks clients to back off for a minute or more. Defaults to 1 minute.</summary>
    public TimeSpan RateLimitDelay { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Reconnect when no data, including the blank keep-alive line X sends about every 20 seconds, arrives for this long.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan KeepAliveTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to wait for X to answer a connection attempt (the response headers). Without it a server that accepts the connection and then goes silent
    /// would hang the stream forever. A timeout counts as a failed attempt and is retried with backoff. Once connected, <see cref="KeepAliveTimeout"/> applies.
    /// Defaults to 30 seconds. The clock is the system clock.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Randomises reconnect delays from 0 (none) to 1: each backoff is multiplied by a random factor between <c>1 - Jitter</c> and 1, and the post-429 wait is
    /// extended by up to <c>Jitter</c> of itself, so many consumers that lose a stream together do not reconnect in lockstep. Defaults to 0.2.
    /// </summary>
    public double Jitter { get; init; } = 0.2;

    /// <summary>Test hook: replaces the random source (a value in [0, 1)).</summary>
    internal Func<double>? RandomSource { get; init; }
}

/// <summary>
/// Streaming endpoints. They require an app-only Bearer token, and the sample and filtered streams depend on your access level.
/// The response is read line by line and never buffered, and dropped connections are re-established automatically.
/// </summary>
public interface IXStreamingClient
{
    /// <summary>Streams a random sample of public Posts (<c>GET /2/tweets/sample/stream</c>).</summary>
    IAsyncEnumerable<XStreamEvent> StreamSampleAsync(XStreamOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Streams Posts that match your rules (<c>GET /2/tweets/search/stream</c>). Add rules first with <see cref="AddRulesAsync"/>.</summary>
    IAsyncEnumerable<XStreamEvent> StreamFilteredAsync(XStreamOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams one of the partitioned Post streams: the 10 percent sample, the firehose or a language firehose. These are Enterprise products;
    /// X answers 403 without that access. Connect once per <paramref name="partition"/> to receive the whole stream.
    /// </summary>
    /// <param name="kind">Which stream.</param>
    /// <param name="partition">The partition number (required by X, starting at 1). The OpenAPI document gives the maximum per stream: 2 for the 10 percent sample and the Japanese, Korean and Portuguese firehoses, 8 for the English firehose, 20 for the full firehose. The SDK does not enforce the maximum, because X may change it.</param>
    /// <param name="options">Fields, backfill and reconnect behaviour.</param>
    /// <param name="cancellationToken">A token to cancel the stream.</param>
    IAsyncEnumerable<XStreamEvent> StreamPartitionAsync(XPostStreamKind kind, int partition, XStreamOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Gets how many filtered-stream rules the project and this app are using (<c>GET /2/tweets/search/stream/rules/counts</c>).</summary>
    Task<XResponse<XRuleCounts>> GetRuleCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the filtered-stream rules.</summary>
    Task<XResponse<IReadOnlyList<XStreamRule>>> GetRulesAsync(IReadOnlyList<string>? ruleIds = null, CancellationToken cancellationToken = default);

    /// <summary>Adds filtered-stream rules. With <paramref name="dryRun"/> X validates them without creating them.</summary>
    Task<XResponse<IReadOnlyList<XStreamRule>>> AddRulesAsync(IReadOnlyList<XStreamRuleDraft> rules, bool dryRun = false, CancellationToken cancellationToken = default);

    /// <summary>Deletes filtered-stream rules by ID.</summary>
    Task<XResponse<IReadOnlyList<XStreamRule>>> DeleteRulesAsync(IReadOnlyList<string> ruleIds, bool dryRun = false, CancellationToken cancellationToken = default);
}

internal sealed class XStreamingClient(XApiTransport transport) : IXStreamingClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public IAsyncEnumerable<XStreamEvent> StreamSampleAsync(XStreamOptions? options = null, CancellationToken cancellationToken = default) =>
        Stream("/2/tweets/sample/stream", options ?? new XStreamOptions(), null, cancellationToken);

    public IAsyncEnumerable<XStreamEvent> StreamFilteredAsync(XStreamOptions? options = null, CancellationToken cancellationToken = default) =>
        Stream("/2/tweets/search/stream", options ?? new XStreamOptions(), null, cancellationToken);

    public IAsyncEnumerable<XStreamEvent> StreamPartitionAsync(XPostStreamKind kind, int partition, XStreamOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(partition, 1);  // the specification requires partition >= 1 (the maximum differs per stream: 2, 8 or 20)
        var route = kind switch
        {
            XPostStreamKind.Sample10 => "/2/tweets/sample10/stream",
            XPostStreamKind.Firehose => "/2/tweets/firehose/stream",
            XPostStreamKind.FirehoseEnglish => "/2/tweets/firehose/stream/lang/en",
            XPostStreamKind.FirehoseJapanese => "/2/tweets/firehose/stream/lang/ja",
            XPostStreamKind.FirehoseKorean => "/2/tweets/firehose/stream/lang/ko",
            XPostStreamKind.FirehosePortuguese => "/2/tweets/firehose/stream/lang/pt",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return Stream(route, options ?? new XStreamOptions(), request => request.Add("partition", partition), cancellationToken);
    }

    public Task<XResponse<XRuleCounts>> GetRuleCountsAsync(CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/tweets/search/stream/rules/counts", XAuthMethods.AppOnly),
            Json.EnvelopeXRuleCounts,
            cancellationToken);

    public Task<XResponse<IReadOnlyList<XStreamRule>>> GetRulesAsync(IReadOnlyList<string>? ruleIds = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/tweets/search/stream/rules", XAuthMethods.AppOnly).Add("ids", ruleIds),
            Json.EnvelopeXStreamRuleList,
            cancellationToken);

    public Task<XResponse<IReadOnlyList<XStreamRule>>> AddRulesAsync(IReadOnlyList<XStreamRuleDraft> rules, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0 || rules.Any(r => string.IsNullOrWhiteSpace(r.Value)))
        {
            throw new ArgumentException("Provide at least one rule, each with a value.", nameof(rules));
        }

        return SendRules(new RulesBody(rules, null), dryRun, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XStreamRule>>> DeleteRulesAsync(IReadOnlyList<string> ruleIds, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ruleIds);
        if (ruleIds.Count == 0)
        {
            throw new ArgumentException("Provide at least one rule ID.", nameof(ruleIds));
        }

        return SendRules(new RulesBody(null, new DeleteRules(ruleIds)), dryRun, cancellationToken);
    }

    private Task<XResponse<IReadOnlyList<XStreamRule>>> SendRules(RulesBody body, bool dryRun, CancellationToken cancellationToken)
    {
        var request = XApiRequest.Create(HttpMethod.Post, "/2/tweets/search/stream/rules", XAuthMethods.AppOnly)
            .Add("dry_run", dryRun ? true : null)
            .WithContent(() => JsonContent.Create(body, Json.RulesBody));
        return transport.SendAsync(request, Json.EnvelopeXStreamRuleList, cancellationToken);
    }

    private async IAsyncEnumerable<XStreamEvent> Stream(
        string route,
        XStreamOptions options,
        Action<XApiRequest>? configure,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (options.KeepAliveTimeout <= TimeSpan.Zero || options.ConnectTimeout <= TimeSpan.Zero || options.ReconnectDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "KeepAliveTimeout, ConnectTimeout and ReconnectDelay must be positive.");
        }

        var failures = 0;
        while (true)
        {
            var request = XApiRequest.Create(HttpMethod.Get, route, XAuthMethods.AppOnly).Add(options.Fields, "tweet.fields");  // the Post streams document tweet.fields only
            request.Add("backfill_minutes", options.BackfillMinutes);
            configure?.Invoke(request);

            var (response, failure) = await TryOpenAsync(request, options.ConnectTimeout, cancellationToken).ConfigureAwait(false);
            TimeSpan? retryAfter = null;

            if (response is not null && response.IsSuccessStatusCode)
            {
                using (response)
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var reader = new StreamReader(stream);
                    while (true)
                    {
                        var (status, line) = await ReadLineAsync(reader, options.KeepAliveTimeout, cancellationToken).ConfigureAwait(false);
                        if (status != ReadStatus.Line)
                        {
                            break; // timeout or end of stream: reconnect.
                        }

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue; // keep-alive. It does not reset the failure count: a server that only ever sends keep-alives and drops must still back off.
                        }

                        var parsed = TryParse(line);
                        if (parsed is not null)
                        {
                            failures = 0;
                            yield return parsed;
                        }
                    }
                }
            }
            else if (response is not null)
            {
                using (response)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var error = XApiTransport.ToError(response, body);
                    var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                    if (!transient)
                    {
                        // 4xx other than 429 (bad credentials, no access, malformed query) will not heal by reconnecting.
                        throw new XApiException(error);
                    }

                    retryAfter = response.StatusCode == HttpStatusCode.TooManyRequests ? options.RateLimitDelay : null;
                    failure = new XApiException(error);
                }
            }

            if (++failures > options.MaxReconnects)
            {
                throw failure ?? new IOException("The stream ended repeatedly without delivering data.");
            }

            var delay = Backoff(failures, options);
            if (retryAfter is { } floor && floor > delay)
            {
                // The post-429 wait is a floor X asked for, so jitter only extends it.
                delay = XRetryPolicy.ExtendWithJitter(floor, options.Jitter, options.RandomSource);
            }

            await Task.Delay(delay, transport.TimeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(HttpResponseMessage? Response, Exception? Failure)> TryOpenAsync(XApiRequest request, TimeSpan connectTimeout, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(connectTimeout);
        try
        {
            return (await transport.OpenStreamAsync(request, linked.Token).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the connect timeout fired; the caller did not cancel.
            return (null, new TimeoutException($"X did not answer the stream connection within {connectTimeout}.", ex));
        }
        catch (HttpRequestException ex)
        {
            return (null, ex);
        }
    }

    private static async Task<(ReadStatus Status, string? Line)> ReadLineAsync(StreamReader reader, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        try
        {
            var line = await reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
            return line is null ? (ReadStatus.Ended, null) : (ReadStatus.Line, line);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (ReadStatus.TimedOut, null);
        }
        catch (IOException)
        {
            return (ReadStatus.Ended, null);
        }
    }

    private static XStreamEvent? TryParse(string line)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize(line, Json.XStreamEnvelope);
            return envelope is null
                ? null
                : new XStreamEvent { Post = envelope.Data, Includes = envelope.Includes, MatchingRules = envelope.MatchingRules, Errors = envelope.Errors };
        }
        catch (JsonException)
        {
            return null; // Skip a malformed line rather than killing a long-lived stream.
        }
    }

    private static TimeSpan Backoff(int failures, XStreamOptions options)
    {
        var delay = TimeSpan.FromTicks((long)(options.ReconnectDelay.Ticks * Math.Pow(2, Math.Min(failures - 1, 16))));
        delay = delay > options.MaxReconnectDelay ? options.MaxReconnectDelay : delay;
        return XRetryPolicy.ApplyJitter(delay, options.Jitter, options.RandomSource);
    }

    private enum ReadStatus
    {
        Line,
        Ended,
        TimedOut,
    }

    internal sealed record RulesBody(IReadOnlyList<XStreamRuleDraft>? Add, DeleteRules? Delete);

    internal sealed record DeleteRules(IReadOnlyList<string> Ids);
}
