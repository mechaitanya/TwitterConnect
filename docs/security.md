# Security

## Secrets

- Credentials are supplied by you at runtime (`XCredentials`, `UseBearerToken`, `XOAuth2Options.ClientSecret`, your `IXTokenStore`). The package contains none, and
  no sample or documentation includes real values. Keep them in a secret store, never in source control or configuration committed to it.
- `XCredentials`, `XOAuth2Token`, `XAuthorizationRequest` and `BearerTokenAuthenticator` override `ToString()` so secrets cannot leak through string interpolation or logging.
- The SDK never logs tokens, secrets, the `Authorization` header, request bodies or response bodies. Its log messages carry only the HTTP method, the route template
  (for example `/2/tweets/{id}`), the status code and the retry attempt. A test asserts this.
- Error messages from the OAuth 2.0 token endpoint include X's `error`/`error_description` and nothing from your request (no verifier, secret or code).

## OAuth 2.0

- `state` (32 random bytes) and the PKCE verifier (48 random bytes, base64url, 64 characters, within the RFC 7636 range of 43-128) come from
  `RandomNumberGenerator`, not `Random`. The challenge is S256 (verified against the RFC 7636 test vector).
- `state` is compared with `CryptographicOperations.FixedTimeEquals`, and `CompleteAuthorizationAsync` refuses to contact the token endpoint when it does not match.
- Refreshes are serialised so single-use refresh tokens are not spent twice; the new token is persisted before it is used.
- Persist tokens encrypted at rest. Store the PKCE verifier and `state` server-side, bound to the user's session, and discard them after use.

## OAuth 1.0a

HMAC-SHA1 is mandated by the protocol and is used only to sign requests; the analyzer suppression for it is documented in the source. Nonces are random GUIDs and
timestamps come from `TimeProvider`.

## Transport

- Requests go to `https://api.x.com/` by default. If you change `BaseAddress`, only point it at a TLS endpoint you trust (a gateway or test server).
- Path parameters are percent-encoded, so a user-supplied ID or username cannot alter the route.
- Streaming and chunked uploads read and write streams incrementally, so a large response cannot exhaust memory.
- Response bodies are size-limited while they are read: `XConnectOptions.MaxResponseBodyBytes` (16 MiB) for API calls, `XStreamOptions.MaxLineBytes` (1 MiB) per stream line. An oversized body throws `XResponseTooLargeException` rather than being truncated; an oversized stream line is skipped and reported in-band.
- Pre-signed transfers have their own time limit, `XConnectOptions.TransferTimeout` (10 minutes).
- Pre-signed compliance upload/download URLs must be HTTPS and are sent **without** your credentials, so your tokens can never reach the storage host.
- `docs/spec-snapshot.json` and `SpecConformanceTests` ensure the SDK never sends an endpoint an authentication method the specification does not list for it.

## Trimming and Native AOT

All JSON goes through one source-generated `JsonSerializerContext`, so no reflection-based serialization is used; the assembly is marked `IsAotCompatible`. Validation
performed for this release:

- `samples/AotCheck` is published with `PublishAot=true` (`dotnet publish samples/AotCheck -c Release`), producing a 5.3 MB native binary that creates a Post, searches, paginates and
  parses a typed error against a stub transport. It exited successfully, and the publish produced **no** trim (`IL2xxx`/`IL3xxx`) or AOT warnings from XConnect. The only
  linker output was warnings about the platform runtime packs.
- The same project enables the trim and AOT analyzers at build time, and `dotnet build` of the library with them enabled is clean.
- Validated on macOS (osx-x64) with the .NET 11 SDK only; other runtime identifiers were not tested.
- `IHttpClientFactory` and `AddXConnect` use factory delegates rather than reflection-based activation.

## Reporting a vulnerability

Please report suspected vulnerabilities privately to the maintainer rather than in a public issue.
