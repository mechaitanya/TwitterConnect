# Error handling

**API failures are values, not exceptions.** Every call returns an `XResponse<T>`; when X rejects it, `IsSuccess` is false and
`Error` explains why. Network failures and programmer errors still throw.

```csharp
var response = await client.Posts.CreateAsync(new CreatePostRequest { Text = "hi" });

switch (response.Error?.Kind)
{
    case null: break;                                   // success
    case XApiErrorKind.DuplicatePost: /* already posted */ break;
    case XApiErrorKind.RateLimited:   Console.WriteLine($"Retry after {response.RateLimit?.ResetsAt:u}"); break;
    case XApiErrorKind.Authorization: /* plan or scope does not allow it */ break;
    default: Console.WriteLine(response.Error); break;
}

var post = response.GetDataOrThrow();   // when you prefer exceptions: throws XApiException
```

## XApiError

`Kind`, `StatusCode`, `Type`, `Title`, `Detail`, `ErrorCode`, `RequestId` and a combined `Message`.

| `XApiErrorKind` | Cause |
| --- | --- |
| `Validation` | HTTP 400 / 422: the request was invalid |
| `Authentication` | HTTP 401: credentials missing, invalid or expired |
| `Authorization` | HTTP 403: valid credentials, but not permitted (scopes, app permissions or your plan) |
| `DuplicatePost` | HTTP 403 with a duplicate-content detail |
| `NotFound` | HTTP 404 |
| `RateLimited` | HTTP 429 |
| `Server` | HTTP 5xx |
| `Serialization` | The response body was not the expected JSON (the HTTP status was otherwise fine) |
| `Conflict` | HTTP 409 or `streaming-connection`: for example a second connection to a single-connection stream |
| `Unknown` | Anything else |

Both X error formats are understood (`{title, detail, type, status}` and the legacy `{errors:[{code, message}]}`), as is a non-JSON body (for
example an HTML page from a proxy), which becomes the `Detail`. `RequestId` is read from the `x-transaction-id` response header when present.

Classification uses X's documented problem `type` first (`invalid-request`, `resource-not-found`, `not-authorized-for-resource`, `client-forbidden`, `usage-capped`,
`rate-limit-exceeded`, `streaming-connection`, `rule-cap`, `invalid-rules`, `duplicate-rules`) and falls back to the HTTP status.

**`DuplicatePost` is a heuristic.** X does not document how it reports a duplicate Post; the SDK recognises HTTP 403 whose detail mentions "duplicate". Treat it as a hint and keep
`Detail` for the exact text. **`RequestId`** comes from the `x-transaction-id` header, which X does not document either; do not rely on it. See [verification-notes.md](verification-notes.md).

## Partial errors

Batch lookups can succeed while reporting individual failures (for example an ID that does not exist). Those arrive in
`response.PartialErrors` and do not make `IsSuccess` false.

## What still throws

| Exception | When |
| --- | --- |
| `ArgumentException` (and subclasses) | The request is invalid or violates a documented limit; thrown before anything is sent |
| `InvalidOperationException` | None of your configured authentication methods is accepted by the endpoint |
| `HttpRequestException` | A network failure (after retries, for idempotent requests) |
| `TimeoutException` | `XConnectOptions.Timeout` elapsed for one attempt, or `XConnectOptions.TransferTimeout` elapsed for a pre-signed compliance upload or download |
| `XResponseTooLargeException` | A response body exceeded `XConnectOptions.MaxResponseBodyBytes` (never truncated; not retried) |
| `OperationCanceledException` | You cancelled |
| `XAuthenticationException` | OAuth 2.0 token missing, refresh rejected, state mismatch, authorization denied |
| `XApiException` | Multi-step operations that cannot return a single result (media uploads, `ItemsAsync`, streaming) and `GetDataOrThrow()` |
| `XMediaProcessingException` | X failed to process uploaded media, or it did not finish in time |

Exceptions are never swallowed. Messages and logs never contain tokens, secrets, `Authorization` headers or request bodies.
