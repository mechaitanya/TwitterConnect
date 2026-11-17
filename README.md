# XConnect

A lightweight, AOT-friendly .NET SDK for the [X API v2](https://docs.x.com/x-api/introduction): publish Posts, upload
media, read timelines and search, manage engagement, send Direct Messages, work with Lists and Spaces, and consume
streams, with OAuth 1.0a, OAuth 2.0 (Authorization Code + PKCE) and app-only authentication.

- Targets **.NET 11** and **C# 15**; nullable reference types on; warnings are errors.
- One NuGet package. Runtime dependencies are limited to `Microsoft.Extensions.Http` (for `IHttpClientFactory`, logging and options); there is no JSON library beyond `System.Text.Json`.
- **System.Text.Json source generation** everywhere. Trimming- and Native AOT-compatible (validated, see [Security & AOT](https://github.com/mechaitanya/XConnect/blob/main/docs/security.md#trimming-and-native-aot)).
- Typed requests and responses, no raw JSON. HTTP failures come back as typed `XApiError`s; they are not thrown.
- Endpoint-aware authentication, safe retries, per-endpoint rate-limit tracking, pagination helpers and resilient streaming.

> **Coverage:** XConnect implements **132 of the 268** operations in the official OpenAPI document (v2.175): Posts, media (including
> subtitles, metadata and analytics), users, engagement, Direct Messages, Lists, Spaces, streaming (including the Enterprise partitioned
> streams), streaming connection management, webhook and Account Activity management, compliance, trends, communities, news and usage. It is **not** a complete mirror of the specification. The generated
> [API coverage matrix](https://github.com/mechaitanya/XConnect/blob/main/docs/api-coverage.md) lists every operation with its status, authentication, scopes, rate-limit scope, access
> notes and test count, and an automated test keeps it in step with the code. [The backlog](https://github.com/mechaitanya/XConnect/blob/main/docs/backlog.md) groups what is missing and why.

> **Unofficial.** XConnect is an independent library and is not affiliated with or endorsed by X Corp.

> **Verification status (2.1.0).** The default tests are stubbed and checked against the official OpenAPI document, and CI builds and tests the library on Linux, macOS and Windows. **No user or Post data operation has been verified against the real X API**: the only live run reached X with an app-only token, and every data call returned HTTP 402 `credits-depleted` (rate-limit headers and the invalid-token 401 were verified; nothing else). Behaviour that depends on X itself (how a duplicate Post, refresh-token rotation or a plan restriction is reported, for example) is unverified. Known limitations: automatic recovery from HTTP 401 is not implemented, and OAuth 2.0 refresh coordination stops at the process boundary. Results from the X API Playground simulator are simulated and are not real X API verification. See [verification notes](https://github.com/mechaitanya/XConnect/blob/main/docs/verification-notes.md).

> **Upgrading from 2.0?** Nothing breaks: `IXClient`, `XClient`, `XCredentials`, `XPostResult` and `XRateLimit` are unchanged.
> See [Migration](https://github.com/mechaitanya/XConnect/blob/main/docs/migration.md).

## Installation

```shell
dotnet add package XConnect.Sdk
```

## Quick start

```csharp
using XConnect;
using XConnect.Models;

var credentials = new XCredentials(
    ConsumerKey: "consumer_key",
    ConsumerSecret: "consumer_secret",
    AccessToken: "access_token",
    AccessTokenSecret: "access_token_secret");

using var http = new HttpClient();
var client = new XConnectClient(http, new XConnectOptions().UseOAuth1(credentials));

var response = await client.Posts.CreateAsync(new CreatePostRequest { Text = "Hello from XConnect!" });

if (response.IsSuccess)
{
    Console.WriteLine($"Posted: https://x.com/i/status/{response.Data!.Id}");
}
else
{
    Console.WriteLine($"{response.Error!.Kind}: {response.Error.Message}");
}
```

Get credentials from the [X Developer Portal](https://developer.x.com): create a project and app, enable **Read and write**
permissions, then generate the keys and tokens. Keep them in a secret store, never in source control.

### Dependency injection

```csharp
services.AddXConnect(options =>
{
    options.BaseAddress = new Uri("https://api.x.com/");
    options.UseOAuth1(credentials);
});

// Add handlers, policies or a custom HttpMessageHandler on the returned IHttpClientBuilder.
```

Then inject `IXConnectClient`. Options are validated; a missing authentication method fails fast.

## The API at a glance

`IXConnectClient` groups the API by area. Every method returns an `XResponse<T>` (data, metadata, expansions, rate limit,
error) unless noted.

| Property | What it does |
| --- | --- |
| `Posts` | Create (media, polls, call-to-actions), reply, quote, edit, thread, delete, hide/unhide replies; get, timelines, mentions, liked Posts, reposts, search, counts, analytics |
| `Media` | Upload images, GIFs, videos and subtitle files (simple or chunked, chosen for you), poll processing, metadata and alt text, subtitles, lookup, analytics |
| `Users` | Look up users, search, followers, following, affiliates |
| `Engagement` | Like, repost, bookmark (and folders), follow, block, mute, with the matching undo operations and lists |
| `DirectMessages` | Send messages, create group conversations, read and delete events |
| `Lists` | Create, update, delete, read, members, followers, memberships, pin, follow |
| `Spaces` | Look up and search Spaces |
| `Streaming` | Sample and filtered streams, stream rules |
| `Webhooks` | Register and validate webhooks, replay, Account Activity subscriptions, filtered-stream links (management only; `XWebhookSecurity` answers CRC checks and verifies signatures) |
| `Connections` | Streaming connection history and termination |
| `Compliance`, `Trends`, `Communities`, `News`, `Usage` | Compliance jobs (create, list, get, cancel, upload, download), trends (WOEID and personalised), Communities, news stories, usage and credit balances |
| `RateLimits` | The last rate limit seen for each endpoint |

Guides: [Authentication](https://github.com/mechaitanya/XConnect/blob/main/docs/authentication.md) ·
[Publishing](https://github.com/mechaitanya/XConnect/blob/main/docs/publishing.md) · [Media](https://github.com/mechaitanya/XConnect/blob/main/docs/media.md) · [Pagination](https://github.com/mechaitanya/XConnect/blob/main/docs/pagination.md) ·
[Error handling](https://github.com/mechaitanya/XConnect/blob/main/docs/error-handling.md) · [Rate limits & retries](https://github.com/mechaitanya/XConnect/blob/main/docs/rate-limits.md) ·
[Streaming](https://github.com/mechaitanya/XConnect/blob/main/docs/streaming.md) · [Webhooks](https://github.com/mechaitanya/XConnect/blob/main/docs/webhooks.md) · [Security & AOT](https://github.com/mechaitanya/XConnect/blob/main/docs/security.md) · [Architecture](https://github.com/mechaitanya/XConnect/blob/main/docs/architecture.md) ·
[API coverage](https://github.com/mechaitanya/XConnect/blob/main/docs/api-coverage.md) · [Backlog](https://github.com/mechaitanya/XConnect/blob/main/docs/backlog.md) · [Verification notes](https://github.com/mechaitanya/XConnect/blob/main/docs/verification-notes.md) ·
[Integration testing](https://github.com/mechaitanya/XConnect/blob/main/docs/integration-testing.md) · [Advertising API](https://github.com/mechaitanya/XConnect/blob/main/docs/advertising-api.md) · [Migration](https://github.com/mechaitanya/XConnect/blob/main/docs/migration.md)

## Publishing with media

```csharp
var media = await client.Media.UploadImageAsync(stream, "announcement.png");

await client.Posts.CreateAsync(new CreatePostRequest
{
    Text = "Our announcement",
    Media = new XPostMedia { MediaIds = [media.MediaId] },
});

var thread = await client.Posts.CreateThreadAsync(["First post", "Second post", "Third post"]);
```

## Things worth knowing

- **Posts are never retried automatically after an ambiguous failure.** If a `POST` fails with a 5xx or a network error, X may have processed it. The SDK returns or throws and leaves the decision to you. See [Rate limits & retries](https://github.com/mechaitanya/XConnect/blob/main/docs/rate-limits.md).
- **Authentication is chosen per endpoint.** Register several methods (for example app-only for search, OAuth 1.0a for posting) and each call uses one that the endpoint accepts. Calling an endpoint that none of your methods can reach throws `InvalidOperationException` before anything is sent.
- **Nothing is requested by default.** Choose `post.fields`, `user.fields` and expansions with `XFieldSelection` and the `XPostFields`/`XUserFields` constants.
- **Access levels matter.** Full-archive search, quote Posting, DMs, compliance and streaming depend on your X plan. X answers `403`, which surfaces as `XApiErrorKind.Authorization`.
- **Diagnostics.** The SDK emits `System.Diagnostics` activities from the `XConnect` `ActivitySource` and logs through `ILogger`. Tokens, secrets, headers and bodies are never logged.

## Development

```shell
dotnet build
dotnet test
```

Requires the .NET 11 SDK. `src/XConnect` is the library, `tests/XConnect.Tests` holds the xUnit tests (all stubbed, so no
network or credentials are needed), `samples/Usage` contains the compiled documentation snippets, and `samples/AotCheck`
is a Native AOT smoke test (`dotnet publish samples/AotCheck -c Release`).

`tests/XConnect.IntegrationTests` is a separate, opt-in suite that talks to the real API (OAuth 1.0a, OAuth 2.0 PKCE and refresh, user lookup, Post
create/lookup/delete, media upload and processing). Every test is skipped by default and the ones that create content need an explicit write phrase;
see [docs/integration-testing.md](https://github.com/mechaitanya/XConnect/blob/main/docs/integration-testing.md). It has not been run for this release.

`tools/generate_coverage.py` regenerates the coverage matrix and the contract snapshot from the official OpenAPI document. The unit tests check every request they
send against that contract (query parameters, types, ranges, request bodies), compare the response models with the documented schemas, and fail if the code, the snapshot and
the matrix disagree. See [verification notes](https://github.com/mechaitanya/XConnect/blob/main/docs/verification-notes.md#how-conformance-is-checked).

## License

MIT. See [LICENSE.txt](https://github.com/mechaitanya/XConnect/blob/main/LICENSE.txt).
