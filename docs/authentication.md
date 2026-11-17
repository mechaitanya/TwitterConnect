# Authentication

X endpoints accept different authentication methods. XConnect models three and picks the right one for each call.

| Method | Class | Typical use |
| --- | --- | --- |
| OAuth 1.0a (HMAC-SHA1) | `OAuth1Authenticator` via `UseOAuth1(credentials)` | Acting as one user with long-lived keys: posting, uploading media |
| OAuth 2.0 Authorization Code + PKCE | `XOAuth2Client`, `OAuth2UserAuthenticator` via `options.OAuth2` | Acting on behalf of many users who authorize your app |
| App-only Bearer token | `BearerTokenAuthenticator` via `UseBearerToken(token)` | Public data: search, lookups, streaming, compliance |

Each endpoint documents what it accepts (see the *Auth* column in [api-coverage.md](api-coverage.md)). For example, bookmark
creation accepts only OAuth 2.0 user context, streaming accepts only app-only, and Spaces accept app-only or OAuth 2.0
but not OAuth 1.0a.

## Endpoint-aware selection

Register as many methods as you have. Registration order is the preference order, and each request uses the first
registered method that the endpoint accepts:

```csharp
var options = new XConnectOptions()
    .UseBearerToken(bearerToken)   // used for search, lookups, streams
    .UseOAuth1(credentials);       // used for posting, uploading media
```

If no registered method is accepted, the call throws `InvalidOperationException` and sends nothing:
`POST /2/tweets accepts OAuth1, OAuth2User authentication, but only AppOnly is configured.`

## OAuth 1.0a

```csharp
var credentials = new XCredentials(consumerKey, consumerSecret, accessToken, accessTokenSecret);
var options = new XConnectOptions().UseOAuth1(credentials);
```

Every attempt is signed afresh (new nonce and timestamp), so a retried request carries a valid signature. JSON and multipart
bodies are not part of the signature, as the OAuth 1.0a specification requires. The timestamp comes from the `TimeProvider`
you give the client, which makes signatures testable.

## OAuth 2.0 (Authorization Code with PKCE)

1. **Create the authorization request.** The SDK generates a cryptographically random `state` and PKCE verifier
   (`RandomNumberGenerator`) and the S256 challenge:

   ```csharp
   var oauth = new XOAuth2Client(http, new XOAuth2Options
   {
       ClientId = "client_id",
       ClientSecret = "client_secret",              // omit for public clients
       RedirectUri = new Uri("https://app.example.com/callback"),
   });

   var authorization = oauth.CreateAuthorizationRequest(
       [XScopes.TweetRead, XScopes.TweetWrite, XScopes.UsersRead, XScopes.OfflineAccess]);
   // Redirect the user to authorization.Uri.
   // Store authorization.State and authorization.CodeVerifier server-side (for example in the user's session).
   ```

2. **Handle the callback.** `CompleteAuthorizationAsync` checks `state` in constant time, rejects denials and exchanges the
   code:

   ```csharp
   XOAuth2Token token = await oauth.CompleteAuthorizationAsync(authorization, callbackUri);
   ```

3. **Use and refresh the token.** Give the client a token store. The authenticator refreshes an access token one minute
   before it expires, serialises concurrent refreshes (refresh tokens may be single-use), and saves the new token:

   ```csharp
   var options = new XConnectOptions
   {
       OAuth2 = new XOAuth2Options { ClientId = "client_id", ClientSecret = "client_secret", RedirectUri = redirect },
       TokenStore = myEncryptedTokenStore,   // implement IXTokenStore
   };
   ```

4. **Revoke when the user disconnects:** `await oauth.RevokeAsync(token.RefreshToken ?? token.AccessToken);`

Confidential clients (with a `ClientSecret`) authenticate to the token endpoint with HTTP Basic; public clients send
`client_id` in the body. Request `XScopes.OfflineAccess` to receive a refresh token, and request only the scopes you need.
Endpoints and parameters follow the [X OAuth 2.0 documentation](https://docs.x.com/fundamentals/authentication/oauth-2-0/user-access-token);
the authorize, token and revoke endpoints are configurable on `XOAuth2Options`.

### Token storage

`IXTokenStore` holds the **current user's** token. `InMemoryXTokenStore` suits tests and short-lived tools. For a real
application, implement `IXTokenStore` over an encrypted store (for example ASP.NET Core Data Protection with a database or
a key vault), and create one client per user. Never write tokens to logs or source control. `XOAuth2Token.ToString()`
masks both tokens.

### What X does not document

X's pages do not say whether refresh tokens **rotate** (a new one returned, the old one invalidated), how long they live, or how a reused one behaves. The SDK therefore adopts a rotated refresh token if one
comes back, keeps the old one if none does, saves the new token **before** using it, serialises concurrent refreshes, and shares one lock per **token store instance** across every authenticator and client that uses that store, whether injected by `AddXConnect` or created by hand. The save uses `CancellationToken.None`, so a caller who cancels cannot lose a rotated token. This cannot coordinate separate processes: give each process its own token or a store that locks externally. Make `SaveTokenAsync` durable:
if X does rotate and your store loses the new token, the user must re-authorise. The opt-in live test `TokenRefresh_...` ([integration-testing.md](integration-testing.md)) reports whether rotation happens for your app.

## App-only

```csharp
var options = new XConnectOptions().UseBearerToken(bearerToken);
```

App-only tokens cannot act for a user. They work for the endpoints marked *App-only* in the coverage matrix.

## Authentication errors

- Local problems (no token stored, refresh rejected, denied authorization, state mismatch) throw `XAuthenticationException`.
- A `401` from the API is reported as `XApiErrorKind.Authentication` on the response.
- A `403` is `XApiErrorKind.Authorization`: valid credentials, but your plan, permissions or scopes do not allow the call.
