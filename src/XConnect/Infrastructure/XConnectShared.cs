using XConnect.Authentication;

namespace XConnect.Infrastructure;

/// <summary>
/// State that must be shared by every client built from the same configuration: the authenticators (so an OAuth 2.0 token is
/// refreshed once, not once per client) and the rate-limit tracker (limits belong to the account, not to a client instance).
/// </summary>
internal sealed class XConnectShared(IReadOnlyList<IXAuthenticator> authenticators, XRateLimitTracker rateLimits)
{
    public IReadOnlyList<IXAuthenticator> Authenticators { get; } = authenticators;

    public XRateLimitTracker RateLimits { get; } = rateLimits;

    /// <summary>Builds the shared state from options. OAuth 2.0 refreshes use <paramref name="oauthHttpClient"/>.</summary>
    public static XConnectShared Create(HttpClient oauthHttpClient, XConnectOptions options, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Validate() is { } problem)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        var clock = timeProvider ?? TimeProvider.System;
        var authenticators = options.BuildAuthenticators(clock);
        if (options.OAuth2 is { } oauth2)
        {
            var oauthClient = new XOAuth2Client(oauthHttpClient, oauth2, clock);
            authenticators.Add(new OAuth2UserAuthenticator(oauthClient, options.TokenStore ?? new InMemoryXTokenStore(), oauth2.RefreshSkew, clock));
        }

        return new XConnectShared(authenticators, new XRateLimitTracker());
    }
}
