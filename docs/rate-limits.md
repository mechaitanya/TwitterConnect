# Rate limits and retries

## Reading rate limits

X reports `x-rate-limit-limit`, `-remaining` and `-reset` per endpoint (and per user or app). XConnect parses them on every response:

```csharp
var response = await client.Posts.SearchRecentAsync("dotnet");
Console.WriteLine($"{response.RateLimit?.Remaining}/{response.RateLimit?.Limit}, resets {response.RateLimit?.ResetsAt:u}");

// Or the latest value seen for any endpoint (keyed by method and route template):
client.RateLimits.TryGet(HttpMethod.Get, "/2/tweets/search/recent", out var limit);
var everything = client.RateLimits.Snapshot();   // "GET /2/tweets/search/recent" -> XRateLimit
```

Limits are **not shared across endpoints**, so the tracker keeps one entry per `METHOD route`. `RateLimit` is `null` when X did not send the headers.
Some endpoints are limited per 24 hours or by plan credits rather than per 15 minutes; follow X's documentation for your plan.

## Retry policy

Retries are on by default (`XConnectOptions.Retry`: `MaxRetries = 2`, `BaseDelay = 500 ms`, `MaxDelay = 30 s`) and follow HTTP safety rules:

| Situation | `GET`, `PUT`, `DELETE` (idempotent) | `POST` (not idempotent) |
| --- | --- | --- |
| HTTP 429 | Retried | Retried (X rejected it before processing) |
| HTTP 500, 502, 503, 504 | Retried | **Not retried** (outcome unknown) |
| Network failure or timeout | Retried | **Not retried**; the exception propagates |

**Creating a Post is a `POST` and is never retried after an ambiguous failure.** If the connection drops or X answers 5xx, the Post may exist. Check
before trying again (for example with `GetUserPostsAsync`). Media uploads, engagement writes and thread creation follow the same rule.

How long to wait: `Retry-After` if present; otherwise, for a 429, until `x-rate-limit-reset`; otherwise exponential backoff
(500 ms, 1 s, ...). **The client never waits indefinitely:** if the required wait is longer than `MaxDelay`, it returns the 429 response immediately and
leaves the decision to you. Waits use `TimeProvider` and honour cancellation.

- **Jitter:** computed backoff is multiplied by a random factor between `1 - Jitter` and 1 (`XRetryOptions.Jitter`, default 0.2; set 0 for exact delays), so clients that fail together do not retry together. Waits X dictates are honoured exactly, never shortened.
- **Clock skew:** when the wait comes from `x-rate-limit-reset`, one second of margin is added and the wait is never below one second, so a local clock that runs ahead of X (reset "in the past") cannot cause an immediate retry.
- **Posts are never retried after an ambiguous failure** (5xx or a lost connection): X may already have created the Post. Only a 429, which X answers before processing, is retried for `POST`.

```csharp
options.Retry = new XRetryOptions { MaxRetries = 0 };                      // no retries at all
options.Retry = new XRetryOptions { MaxRetries = 4, MaxDelay = TimeSpan.FromMinutes(2) };
```

Each attempt is re-authenticated (OAuth 1.0a gets a fresh nonce). Retries are logged at `Debug` level without secrets.

`Timeout` (default 100 s) applies to each attempt and is skipped for streaming.

## Concurrency

X documents limits per endpoint, per user (user tokens) or per app (Bearer), and does not describe how parallel requests share them. XConnect therefore does **not** throttle by default: a burst of parallel calls can exhaust a
window before any response reports it, and each call retries independently. Two safeguards are available:

- `XConnectOptions.MaxConcurrentRequests` caps requests in flight through a client (a slot is released as soon as a response is read, and also when a call fails).
- `client.RateLimits` is safe under parallel use. Responses can finish out of order, so for each endpoint it keeps the **newest window** and, within a window, the **lowest `Remaining`**; a slow older response can never make the quota look larger.

Injected clients (`AddXConnect`) share one tracker and one set of authenticators, so the figures are per account, not per injected instance.
