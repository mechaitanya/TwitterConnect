namespace XConnect.Authentication;

/// <summary>The authentication strategies the X API supports. An endpoint accepts a subset.</summary>
[Flags]
public enum XAuthMethods
{
    /// <summary>No method.</summary>
    None = 0,

    /// <summary>OAuth 1.0a user context.</summary>
    OAuth1 = 1,

    /// <summary>OAuth 2.0 Authorization Code with PKCE (user context).</summary>
    OAuth2User = 2,

    /// <summary>App-only Bearer token.</summary>
    AppOnly = 4,

    /// <summary>Any user-context method.</summary>
    UserContext = OAuth1 | OAuth2User,

    /// <summary>Any method.</summary>
    Any = OAuth1 | OAuth2User | AppOnly,
}

/// <summary>Adds credentials to an outgoing X API request.</summary>
public interface IXAuthenticator
{
    /// <summary>The strategy this authenticator implements. It is only used for endpoints that accept it.</summary>
    XAuthMethods Method { get; }

    /// <summary>Adds the <c>Authorization</c> header. Called once per attempt, so signatures stay fresh across retries.</summary>
    /// <param name="request">The request to authenticate. Its URI is final.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

/// <summary>Signs requests with OAuth 1.0a (HMAC-SHA1) using <see cref="XCredentials"/>.</summary>
public sealed class OAuth1Authenticator : IXAuthenticator
{
    private readonly XCredentials _credentials;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the authenticator.</summary>
    /// <param name="credentials">The four OAuth 1.0a values.</param>
    /// <param name="timeProvider">Clock for the timestamp; defaults to the system clock.</param>
    public OAuth1Authenticator(XCredentials credentials, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        credentials.Validate();
        _credentials = credentials;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public XAuthMethods Method => XAuthMethods.OAuth1;

    /// <inheritdoc />
    public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uri = request.RequestUri ?? throw new InvalidOperationException("The request has no URI.");

        request.Headers.TryAddWithoutValidation("Authorization", OAuth1Signer.CreateAuthorizationHeader(
            request.Method,
            uri,
            _credentials,
            nonce: Guid.NewGuid().ToString("N"),
            timestamp: _timeProvider.GetUtcNow().ToUnixTimeSeconds()));
        return ValueTask.CompletedTask;
    }
}

/// <summary>Authenticates with an app-only Bearer token.</summary>
public sealed class BearerTokenAuthenticator : IXAuthenticator
{
    private readonly string _token;

    /// <summary>Creates the authenticator.</summary>
    /// <param name="bearerToken">The app's Bearer token from the X Developer Portal.</param>
    public BearerTokenAuthenticator(string bearerToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        _token = bearerToken;
    }

    /// <inheritdoc />
    public XAuthMethods Method => XAuthMethods.AppOnly;

    /// <inheritdoc />
    public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
        return ValueTask.CompletedTask;
    }

    /// <summary>Masks the token.</summary>
    public override string ToString() => nameof(BearerTokenAuthenticator) + " { *** }";
}
