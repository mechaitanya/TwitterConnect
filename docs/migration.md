# Migration

## From XConnect 2.0 to 2.1

Nothing is removed or changed. These 2.0 types and members still exist and behave identically:

`IXClient.CreatePostAsync`, `XClient` (both constructors, `CreatePostUri`), `XCredentials`, `XPostResult`, `XRateLimit`.

```csharp
IXClient client = new XClient();                       // still works
XPostResult result = await client.CreatePostAsync(credentials, "Hello from XConnect!");
```

The new SDK is additive. You can adopt it gradually:

| 2.0 | 2.1 equivalent |
| --- | --- |
| `new XClient().CreatePostAsync(creds, text)` returning `XPostResult` | `new XConnectClient(http, new XConnectOptions().UseOAuth1(creds)).Posts.CreateAsync(new CreatePostRequest { Text = text })` returning `XResponse<XCreatedPost>` |
| `result.IsSuccess`, `result.PostId`, `result.Error` (string) | `response.IsSuccess`, `response.Data.Id`, `response.Error` (`XApiError` with `Kind`, `Detail`, ...) |
| `result.RateLimit` | `response.RateLimit` and `client.RateLimits` |
| `services.AddHttpClient<IXClient, XClient>()` | `services.AddXClient()` (same thing), or `services.AddXConnect(...)` for the full SDK |

Differences in behaviour worth knowing when you switch:

- `XClient` signs each call with the credentials you pass and does not retry. `XConnectClient` is configured once with its authentication, retries `GET`/`PUT`/`DELETE` and 429s (never a failed `POST`), and picks an authentication method per endpoint.
- `XClient` did not validate input beyond non-empty text; `XConnectClient` validates documented limits and throws `ArgumentException` before sending.
- The package gained a dependency on `Microsoft.Extensions.Http` (for `IHttpClientFactory`, logging and options).

## Interfaces grew (2.1.0, additive but relevant to mocks)

The 2.0 types above are untouched. The new 2.1 interfaces gained members while 2.1 was in development, and 2.1.0 has not shipped, so this only matters if you built against a pre-release:

- `IXConnectClient` has the properties `News`, `Usage`, `Webhooks` and `Connections` (in addition to the original areas).
- `IXUsersClient` has `GetMyFollowersAsync`, `GetMyFollowingAsync`, `GetAffiliatesAsync` and the bookmark-folder members; `IXSpacesClient` has `GetTicketBuyersAsync`.
- Response records gained properties (for example `XPost.OrganicMetrics`, `XUser.Entities`). They are init-only and nullable, so existing code compiles and deserialises unchanged.

These interfaces are meant to be **consumed**. If you implement or mock them (for tests), expect new members in each minor version and prefer wrapping them behind your own interface. A mock built with a mocking library
needs no change; a hand-written implementation does.

## From TwitterConnect 1.x

| 1.x | 2.x |
| --- | --- |
| `TwitterConnect` namespace / package | `XConnect` |
| `MyTwitterConnect` | `XClient` (implements `IXClient`), or `XConnectClient` |
| `Credentials` (mutable class) | `XCredentials` (immutable record) |
| `AuthenticateUserAndSendTweet(creds, msg)` returning `HttpResponseMessage?` | `CreatePostAsync(creds, text, ct)` returning `XPostResult` |
| Exceptions swallowed, `null` returned | Argument errors throw; HTTP failures are reported in the result |
| `api.twitter.com`, Newtonsoft.Json | `api.x.com`, `System.Text.Json` |

## Test packages (development only)

The test project moved from FluentAssertions to **AwesomeAssertions**, a drop-in fork that keeps an Apache-2.0 licence (FluentAssertions 8 and later is commercially licensed).
