using System.Collections.Concurrent;
using XConnect.Authentication;

namespace XConnect;

/// <summary>Configuration for <see cref="XConnectClient"/>.</summary>
public sealed class XConnectOptions
{
    private readonly List<Func<TimeProvider, IXAuthenticator>> _authenticators = [];

    /// <summary>The API base address. Defaults to <c>https://api.x.com/</c>.</summary>
    public Uri BaseAddress { get; set; } = new("https://api.x.com/");

    /// <summary>The per-attempt request timeout. Streaming connections are exempt. Defaults to 100 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// The maximum number of requests in flight at once through one client, or <see langword="null"/> (the default) for no limit.
    /// X's documentation does not describe how concurrent requests share a rate limit: each response simply reports the remaining quota,
    /// so a burst of parallel calls can exhaust a window before any response says so. Set a cap if you issue many calls in parallel.
    /// Streaming connections are not counted.
    /// </summary>
    public int? MaxConcurrentRequests { get; set; }

    /// <summary>Retry behaviour for transient failures.</summary>
    public XRetryOptions Retry { get; set; } = new();

    /// <summary>
    /// The numeric ID of the authenticated user. When set, engagement calls (like, follow, bookmark and so on) skip the
    /// <c>GET /2/users/me</c> lookup that otherwise resolves it.
    /// </summary>
    public string? AuthenticatedUserId { get; set; }

    /// <summary>OAuth 2.0 application settings. When set, an <see cref="OAuth2UserAuthenticator"/> is added using <see cref="TokenStore"/>.</summary>
    public XOAuth2Options? OAuth2 { get; set; }

    /// <summary>Where OAuth 2.0 tokens are kept. Defaults to an in-memory store when <see cref="OAuth2"/> is set.</summary>
    public IXTokenStore? TokenStore { get; set; }

    /// <summary>Adds OAuth 1.0a user-context authentication.</summary>
    public XConnectOptions UseOAuth1(XCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        credentials.Validate();
        _authenticators.Add(clock => new OAuth1Authenticator(credentials, clock));
        return this;
    }

    /// <summary>Adds app-only Bearer token authentication.</summary>
    public XConnectOptions UseBearerToken(string bearerToken)
    {
        var authenticator = new BearerTokenAuthenticator(bearerToken);
        _authenticators.Add(_ => authenticator);
        return this;
    }

    /// <summary>Adds a custom authenticator.</summary>
    public XConnectOptions UseAuthenticator(IXAuthenticator authenticator)
    {
        ArgumentNullException.ThrowIfNull(authenticator);
        _authenticators.Add(_ => authenticator);
        return this;
    }

    internal List<IXAuthenticator> BuildAuthenticators(TimeProvider timeProvider) =>
        [.. _authenticators.Select(create => create(timeProvider))];

    /// <summary>Returns a description of the first problem with the options, or <see langword="null"/> when they are valid.</summary>
    public string? Validate()
    {
        if (!BaseAddress.IsAbsoluteUri)
        {
            return "BaseAddress must be an absolute URI.";
        }

        if (Timeout <= TimeSpan.Zero && Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            return "Timeout must be positive or Timeout.InfiniteTimeSpan.";
        }

        if (MaxConcurrentRequests is < 1)
        {
            return "MaxConcurrentRequests must be at least 1 when set.";
        }

        if (Retry.MaxRetries < 0 || Retry.BaseDelay < TimeSpan.Zero || Retry.MaxDelay < TimeSpan.Zero)
        {
            return "Retry values must not be negative.";
        }

        if (_authenticators.Count == 0 && OAuth2 is null)
        {
            return "Configure at least one authentication method (UseOAuth1, UseBearerToken, OAuth2 or UseAuthenticator).";
        }

        if (OAuth2 is { } oauth && string.IsNullOrWhiteSpace(oauth.ClientId))
        {
            return "OAuth2.ClientId is required.";
        }

        return null;
    }
}

/// <summary>Retry settings.</summary>
public sealed record XRetryOptions
{
    /// <summary>The maximum number of retries after the first attempt. <c>0</c> disables retries. Defaults to 2.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>The first backoff delay when X gives no hint. It doubles on every retry. Defaults to 500 ms.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The longest single wait the client accepts. If <c>Retry-After</c> or the rate-limit reset asks for more,
    /// the response is returned instead of waiting. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Randomisation of the exponential backoff, from 0 (none) to 1: each computed delay is multiplied by a random factor between <c>1 - Jitter</c> and 1,
    /// so many clients that fail together do not retry in lockstep. Delays X dictates (<c>Retry-After</c>, the rate-limit reset) are honoured exactly.
    /// Defaults to 0.2.
    /// </summary>
    public double Jitter { get; init; } = 0.2;

    /// <summary>Test hook: replaces the random source (a value in [0, 1)).</summary>
    internal Func<double>? RandomSource { get; init; }
}

/// <summary>
/// Remembers the latest rate-limit headers seen for each endpoint. Rate limits are per endpoint, so each is keyed
/// by HTTP method and route template, for example <c>GET /2/users/{id}/tweets</c>.
/// </summary>
public sealed class XRateLimitTracker
{
    private readonly ConcurrentDictionary<string, XRateLimit> _limits = new(StringComparer.Ordinal);

    /// <summary>Gets the last rate limit seen for an endpoint.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="route">The route template, for example <c>/2/tweets</c>.</param>
    /// <param name="rateLimit">The limit, when one has been seen.</param>
    public bool TryGet(HttpMethod method, string route, out XRateLimit? rateLimit)
    {
        ArgumentNullException.ThrowIfNull(method);
        var found = _limits.TryGetValue(Key(method, route), out var value);
        rateLimit = found ? value : null;
        return found;
    }

    /// <summary>A copy of every limit seen so far, keyed by <c>METHOD route</c>.</summary>
    public IReadOnlyDictionary<string, XRateLimit> Snapshot() => new Dictionary<string, XRateLimit>(_limits, StringComparer.Ordinal);

    /// <summary>
    /// Records a limit seen on a response. Responses can complete out of order, so the newest window wins and, within one window, the lowest
    /// <c>Remaining</c> wins; otherwise a slow older response could make the quota look larger than it is.
    /// </summary>
    internal void Record(HttpMethod method, string route, XRateLimit limit) =>
        _limits.AddOrUpdate(
            Key(method, route),
            limit,
            (_, current) =>
                limit.ResetsAt > current.ResetsAt ? limit
                : limit.ResetsAt == current.ResetsAt && limit.Remaining < current.Remaining ? limit
                : current);

    private static string Key(HttpMethod method, string route) => method.Method + " " + route;
}
