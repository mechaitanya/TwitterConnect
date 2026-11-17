using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XConnect.Authentication;

/// <summary>Well-known OAuth 2.0 scope names from the X API OpenAPI specification.</summary>
public static class XScopes
{
    /// <summary>Read Posts and profiles.</summary>
    public const string TweetRead = "tweet.read";

    /// <summary>Create and delete Posts, reposts and replies.</summary>
    public const string TweetWrite = "tweet.write";

    /// <summary>Hide and unhide replies.</summary>
    public const string TweetModerateWrite = "tweet.moderate.write";

    /// <summary>Read users.</summary>
    public const string UsersRead = "users.read";

    /// <summary>Read follows.</summary>
    public const string FollowsRead = "follows.read";

    /// <summary>Follow and unfollow.</summary>
    public const string FollowsWrite = "follows.write";

    /// <summary>Read likes.</summary>
    public const string LikeRead = "like.read";

    /// <summary>Like and unlike.</summary>
    public const string LikeWrite = "like.write";

    /// <summary>Read bookmarks.</summary>
    public const string BookmarkRead = "bookmark.read";

    /// <summary>Create and delete bookmarks.</summary>
    public const string BookmarkWrite = "bookmark.write";

    /// <summary>Read blocked accounts.</summary>
    public const string BlockRead = "block.read";

    /// <summary>Block and unblock.</summary>
    public const string BlockWrite = "block.write";

    /// <summary>Read muted accounts.</summary>
    public const string MuteRead = "mute.read";

    /// <summary>Mute and unmute.</summary>
    public const string MuteWrite = "mute.write";

    /// <summary>Read Lists.</summary>
    public const string ListRead = "list.read";

    /// <summary>Manage Lists.</summary>
    public const string ListWrite = "list.write";

    /// <summary>Read Spaces.</summary>
    public const string SpaceRead = "space.read";

    /// <summary>Read Direct Messages.</summary>
    public const string DmRead = "dm.read";

    /// <summary>Send and delete Direct Messages.</summary>
    public const string DmWrite = "dm.write";

    /// <summary>Upload media.</summary>
    public const string MediaWrite = "media.write";

    /// <summary>Read the reverse-chronological timeline and reposts of the user.</summary>
    public const string TimelineRead = "timeline.read";

    /// <summary>Receive a refresh token.</summary>
    public const string OfflineAccess = "offline.access";
}

/// <summary>An OAuth 2.0 user access token and its metadata.</summary>
public sealed record XOAuth2Token
{
    /// <summary>The access token. Treat as a secret.</summary>
    public required string AccessToken { get; init; }

    /// <summary>The refresh token, present when <see cref="XScopes.OfflineAccess"/> was granted. Treat as a secret.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>When the access token expires, or <see langword="null"/> if X did not say.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The scopes granted.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>The token type, normally <c>bearer</c>.</summary>
    public string TokenType { get; init; } = "bearer";

    /// <summary>True when the access token expires within <paramref name="skew"/> of <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now, TimeSpan skew) => ExpiresAt is { } at && at - skew <= now;

    /// <summary>Masks the secrets so the token never leaks into logs.</summary>
    public override string ToString() => $"{nameof(XOAuth2Token)} {{ ExpiresAt = {ExpiresAt:u}, Scopes = [{string.Join(' ', Scopes)}], *** }}";
}

/// <summary>Persists OAuth 2.0 tokens. Implement it over a secret store; never write tokens to logs or source control.</summary>
public interface IXTokenStore
{
    /// <summary>Loads the current token, or <see langword="null"/> when none is stored.</summary>
    ValueTask<XOAuth2Token?> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>Saves a token, replacing the previous one.</summary>
    ValueTask SaveTokenAsync(XOAuth2Token token, CancellationToken cancellationToken);
}

/// <summary>An in-process <see cref="IXTokenStore"/>, suitable for tests and short-lived tools.</summary>
public sealed class InMemoryXTokenStore : IXTokenStore
{
    private XOAuth2Token? _token;

    /// <summary>Creates a store, optionally seeded with a token.</summary>
    public InMemoryXTokenStore(XOAuth2Token? initial = null) => _token = initial;

    /// <inheritdoc />
    public ValueTask<XOAuth2Token?> GetTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Volatile.Read(ref _token));

    /// <inheritdoc />
    public ValueTask SaveTokenAsync(XOAuth2Token token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        Volatile.Write(ref _token, token);
        return ValueTask.CompletedTask;
    }
}

/// <summary>OAuth 2.0 application settings.</summary>
public sealed record XOAuth2Options
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        // The generated ToString would print ClientSecret.
        builder.Append("ClientId = ").Append(ClientId).Append(", ClientSecret = ").Append(ClientSecret is null ? "(none)" : "***");
        return true;
    }

    /// <summary>The OAuth 2.0 Client ID. Required.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The Client Secret. Set it for confidential clients; leave <see langword="null"/> for public clients.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>The registered callback URL. Required.</summary>
    public Uri? RedirectUri { get; set; }

    /// <summary>The user authorization page.</summary>
    public Uri AuthorizationEndpoint { get; set; } = new("https://x.com/i/oauth2/authorize");

    /// <summary>The token endpoint.</summary>
    public Uri TokenEndpoint { get; set; } = new("https://api.x.com/2/oauth2/token");

    /// <summary>The revocation endpoint.</summary>
    public Uri RevocationEndpoint { get; set; } = new("https://api.x.com/2/oauth2/revoke");

    /// <summary>Refresh the access token this long before it expires.</summary>
    public TimeSpan RefreshSkew { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>An authorization request: send the user to <see cref="Uri"/> and keep the rest until the callback.</summary>
public sealed record XAuthorizationRequest
{
    /// <summary>The URL to open in the user's browser.</summary>
    public required Uri Uri { get; init; }

    /// <summary>The anti-forgery <c>state</c> to compare with the callback.</summary>
    public required string State { get; init; }

    /// <summary>The PKCE code verifier. Keep it server-side and secret until the code exchange.</summary>
    public required string CodeVerifier { get; init; }

    /// <summary>Masks the verifier.</summary>
    public override string ToString() => $"{nameof(XAuthorizationRequest)} {{ Uri = {Uri}, *** }}";
}

/// <summary>Implements the OAuth 2.0 Authorization Code flow with PKCE (S256) against X.</summary>
public sealed class XOAuth2Client
{
    private readonly HttpClient _httpClient;
    private readonly XOAuth2Options _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the client.</summary>
    /// <param name="httpClient">The HTTP client to use.</param>
    /// <param name="options">Application settings.</param>
    /// <param name="timeProvider">Clock used to compute expiry; defaults to the system clock.</param>
    public XOAuth2Client(HttpClient httpClient, XOAuth2Options options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId, nameof(options));
        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Builds the authorization URL, generating a secure <c>state</c> and PKCE verifier.</summary>
    /// <param name="scopes">The scopes to request. Add <see cref="XScopes.OfflineAccess"/> to receive a refresh token.</param>
    public XAuthorizationRequest CreateAuthorizationRequest(IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var redirect = _options.RedirectUri ?? throw new InvalidOperationException("XOAuth2Options.RedirectUri is required to start an authorization.");
        var scopeList = scopes.Distinct(StringComparer.Ordinal).ToArray();
        if (scopeList.Length == 0)
        {
            throw new ArgumentException("At least one scope is required.", nameof(scopes));
        }

        var state = CreateRandomToken(32);
        var verifier = CreateRandomToken(48);

        var query = string.Join('&',
            Pair("response_type", "code"),
            Pair("client_id", _options.ClientId),
            Pair("redirect_uri", redirect.AbsoluteUri),
            Pair("scope", string.Join(' ', scopeList)),
            Pair("state", state),
            Pair("code_challenge", CreateCodeChallenge(verifier)),
            Pair("code_challenge_method", "S256"));

        var builder = new UriBuilder(_options.AuthorizationEndpoint) { Query = query };
        return new XAuthorizationRequest { Uri = builder.Uri, State = state, CodeVerifier = verifier };
    }

    /// <summary>Compares the callback <c>state</c> with the expected value in constant time.</summary>
    public static bool ValidateState(string expectedState, string? actualState)
    {
        if (string.IsNullOrEmpty(expectedState) || string.IsNullOrEmpty(actualState))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedState), Encoding.UTF8.GetBytes(actualState));
    }

    /// <summary>The S256 code challenge for a verifier: <c>BASE64URL(SHA256(verifier))</c>.</summary>
    public static string CreateCodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(codeVerifier);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    /// <summary>Validates the callback and exchanges its authorization code for tokens.</summary>
    /// <param name="request">The request created by <see cref="CreateAuthorizationRequest"/>.</param>
    /// <param name="callbackUri">The full URL X redirected the user to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="XAuthenticationException">The state did not match, the user denied access, or the exchange failed.</exception>
    public async Task<XOAuth2Token> CompleteAuthorizationAsync(XAuthorizationRequest request, Uri callbackUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(callbackUri);

        var parameters = ParseQuery(callbackUri.Query);
        if (!ValidateState(request.State, parameters.GetValueOrDefault("state")))
        {
            throw new XAuthenticationException("The OAuth 2.0 state did not match; the callback may be forged.");
        }

        if (parameters.TryGetValue("error", out var error))
        {
            throw new XAuthenticationException($"Authorization was not granted: {error}.");
        }

        if (!parameters.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
        {
            throw new XAuthenticationException("The callback did not contain an authorization code.");
        }

        return await ExchangeCodeAsync(code, request.CodeVerifier, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Exchanges an authorization code for tokens.</summary>
    public Task<XOAuth2Token> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);
        var redirect = _options.RedirectUri ?? throw new InvalidOperationException("XOAuth2Options.RedirectUri is required.");

        return RequestTokenAsync(
        [
            KeyValuePair.Create("grant_type", "authorization_code"),
            KeyValuePair.Create("code", code),
            KeyValuePair.Create("redirect_uri", redirect.AbsoluteUri),
            KeyValuePair.Create("code_verifier", codeVerifier),
        ], previous: null, cancellationToken);
    }

    /// <summary>Uses the refresh token to obtain a new access token.</summary>
    /// <exception cref="XAuthenticationException">The token has no refresh token or X rejected the refresh.</exception>
    public Task<XOAuth2Token> RefreshAsync(XOAuth2Token token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            throw new XAuthenticationException("The token has no refresh token. Request the 'offline.access' scope to receive one.");
        }

        return RequestTokenAsync(
        [
            KeyValuePair.Create("grant_type", "refresh_token"),
            KeyValuePair.Create("refresh_token", token.RefreshToken),
        ], previous: token, cancellationToken);
    }

    /// <summary>Revokes an access or refresh token.</summary>
    /// <exception cref="XAuthenticationException">X rejected the revocation.</exception>
    public async Task RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        using var request = CreateFormRequest(_options.RevocationEndpoint, [KeyValuePair.Create("token", token)]);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new XAuthenticationException($"Token revocation failed with HTTP {(int)response.StatusCode}.");
        }
    }

    private async Task<XOAuth2Token> RequestTokenAsync(List<KeyValuePair<string, string>> form, XOAuth2Token? previous, CancellationToken cancellationToken)
    {
        using var request = CreateFormRequest(_options.TokenEndpoint, form);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryParse(body);
            var reason = error?.ErrorDescription ?? error?.Error ?? "no detail";
            throw new XAuthenticationException($"The token endpoint returned HTTP {(int)response.StatusCode}: {reason}");
        }

        var parsed = TryParse(body);
        if (parsed?.AccessToken is not { Length: > 0 } accessToken)
        {
            throw new XAuthenticationException("The token endpoint response did not contain an access token.");
        }

        return new XOAuth2Token
        {
            AccessToken = accessToken,
            RefreshToken = parsed.RefreshToken ?? previous?.RefreshToken,
            ExpiresAt = parsed.ExpiresIn is { } seconds ? _timeProvider.GetUtcNow().AddSeconds(Math.Clamp(seconds, 0, 31_536_000)) : null,
            Scopes = parsed.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? previous?.Scopes ?? [],
            TokenType = parsed.TokenType ?? "bearer",
        };
    }

    private HttpRequestMessage CreateFormRequest(Uri endpoint, List<KeyValuePair<string, string>> form)
    {
        if (string.IsNullOrEmpty(_options.ClientSecret))
        {
            form.Add(KeyValuePair.Create("client_id", _options.ClientId));
        }

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };

        if (!string.IsNullOrEmpty(_options.ClientSecret))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{Uri.EscapeDataString(_options.ClientId)}:{Uri.EscapeDataString(_options.ClientSecret)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        return request;
    }

    private static TokenResponse? TryParse(string body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, XOAuth2JsonContext.Default.TokenResponse);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CreateRandomToken(int byteCount) => Base64Url(RandomNumberGenerator.GetBytes(byteCount));

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Pair(string key, string value) => $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=', StringComparison.Ordinal);
            var key = index < 0 ? pair : pair[..index];
            var value = index < 0 ? string.Empty : pair[(index + 1)..];
            result[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return result;
    }

    internal sealed record TokenResponse
    {
        public string? AccessToken { get; init; }

        public string? RefreshToken { get; init; }

        public long? ExpiresIn { get; init; }

        public string? Scope { get; init; }

        public string? TokenType { get; init; }

        public string? Error { get; init; }

        public string? ErrorDescription { get; init; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(XOAuth2Client.TokenResponse))]
internal sealed partial class XOAuth2JsonContext : JsonSerializerContext;

/// <summary>
/// Authenticates user-context requests with an OAuth 2.0 access token, refreshing it transparently
/// when it is about to expire and persisting the refreshed token to the <see cref="IXTokenStore"/>.
/// </summary>
public sealed class OAuth2UserAuthenticator : IXAuthenticator, IDisposable
{
    private readonly XOAuth2Client _client;
    private readonly IXTokenStore _store;
    private readonly TimeSpan _skew;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock;

    // One lock per token store instance, shared by every authenticator (and client) that uses that store, so a refresh token that rotates
    // is never spent twice in this process. The table holds the store weakly. It cannot coordinate separate processes.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IXTokenStore, SemaphoreSlim> RefreshLocks = [];

    /// <summary>Creates the authenticator.</summary>
    /// <param name="client">The OAuth 2.0 client used to refresh tokens.</param>
    /// <param name="store">Where the current token lives.</param>
    /// <param name="refreshSkew">Refresh this long before expiry. Defaults to one minute.</param>
    /// <param name="timeProvider">Clock; defaults to the system clock.</param>
    public OAuth2UserAuthenticator(XOAuth2Client client, IXTokenStore store, TimeSpan? refreshSkew = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        _client = client;
        _store = store;
        _refreshLock = RefreshLocks.GetValue(store, static _ => new SemaphoreSlim(1, 1));
        _skew = refreshSkew ?? TimeSpan.FromMinutes(1);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public XAuthMethods Method => XAuthMethods.OAuth2User;

    /// <inheritdoc />
    public async ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = await GetValidTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token.AccessToken);
    }

    /// <summary>Nothing to release: the refresh lock belongs to the token store and is shared with other authenticators.</summary>
    public void Dispose()
    {
    }

    private async ValueTask<XOAuth2Token> GetValidTokenAsync(CancellationToken cancellationToken)
    {
        var token = await _store.GetTokenAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new XAuthenticationException("No OAuth 2.0 token is available. Complete the authorization flow and save the token first.");

        if (!token.IsExpired(_timeProvider.GetUtcNow(), _skew))
        {
            return token;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed while this one waited.
            token = await _store.GetTokenAsync(cancellationToken).ConfigureAwait(false) ?? token;
            if (!token.IsExpired(_timeProvider.GetUtcNow(), _skew))
            {
                return token;
            }

            var refreshed = await _client.RefreshAsync(token, cancellationToken).ConfigureAwait(false);
            // The refresh token was spent by the call above, so persisting the replacement must not be abandoned because the caller cancelled.
            await _store.SaveTokenAsync(refreshed, CancellationToken.None).ConfigureAwait(false);
            return refreshed;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Not used; present so the type formats secrets safely.</summary>
    public override string ToString() => nameof(OAuth2UserAuthenticator) + string.Create(CultureInfo.InvariantCulture, $" {{ Skew = {_skew} }}");
}
