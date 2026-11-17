# Architecture

One NuGet package, organised internally by responsibility. Every API area is a small client on top of one shared pipeline.

```
src/XConnect/
  XClient, IXClient, XCredentials, XPostResult   Version 2.0 API (unchanged)
  XConnectClient, IXConnectClient                Entry point: Posts, Users, Media, Engagement, DirectMessages, Lists, Spaces, Streaming, ...
  XResponse<T>, XApiError, XApiException         Typed results and the error model
  XPaginator                                     Reusable page and item traversal
  Authentication/                                IXAuthenticator; OAuth 1.0a, app-only, OAuth 2.0 (XOAuth2Client, OAuth2UserAuthenticator, token store)
  Infrastructure/                                XApiTransport (retry, rate limits, error parsing, logging, tracing), XApiRequest, options, rate-limit tracker
  Clients/                                       One file per API area (interface + internal implementation)
  Streaming/                                     Streaming client and event types
  Models/                                        Wire and request models, queries and field-name constants
  Serialization/                                 The single source-generated JsonSerializerContext
  Extensions/                                    AddXConnect / AddXClient
```

**Request path.** A client builds an `XApiRequest` (method, route template, the authentication methods the endpoint accepts, query, body factory) and hands it to `XApiTransport`. The
transport picks the first configured authenticator the endpoint accepts, authenticates *each attempt*, sends, reads rate-limit headers into `XRateLimitTracker`, applies the retry
policy, and parses the JSON envelope (`data`, `meta`, `includes`, `errors`) into an `XResponse<T>`.

**Design decisions.**
- *Results over exceptions for API failures.* The response always carries the status, error and rate limit, so callers can react to a 429 or 403 without catching.
- *Endpoint-declared authentication.* Each call declares what the specification allows; the SDK never guesses.
- *Retry safety lives in one place* (`XRetryPolicy`): idempotent methods and 429 only. See [rate-limits.md](rate-limits.md).
- *Bodies are factories*, so a retried request gets fresh content, and OAuth 1.0a is re-signed.
- *Source-generated JSON only* (snake_case naming, nulls omitted, unknown fields ignored, numbers readable from strings). Adding a model means adding it to `XConnectJsonContext`.
- *No abstraction without a user.* There is no generic "resource client" layer and no empty placeholder types; each client is explicit, which keeps the surface greppable and AOT-safe.

**Specification conformance.** `tools/generate_coverage.py` turns the official OpenAPI document into `docs/spec-snapshot.json` and the generated `docs/api-coverage.md`. `SpecConformanceTests` scans the source for every route the SDK calls and checks it against the snapshot (route, method, authentication subset) and against the matrix, so the code, spec and documentation cannot drift apart silently.

**Compatibility.** `XClient` keeps its own simple path (no retries, signs per call). It shares `OAuth1Signer` with the new SDK, so the signing code is exercised by both test suites.

**Testing.** All tests use a queued stub `HttpMessageHandler` and a `TimeProvider` whose timers fire instantly, so retry/backoff/refresh logic is deterministic and instantaneous.
