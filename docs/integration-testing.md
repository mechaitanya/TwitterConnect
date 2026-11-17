# Integration testing against the real X API

`tests/XConnect.IntegrationTests` is separate from the unit tests. **Every test is skipped unless you opt in**, so `dotnet test` on the solution never contacts X. Nothing here is run in
CI, and nothing reads or writes secrets in the repository: credentials come from environment variables and tokens are saved only to files **you** name (owner-read-only on Unix).

```shell
export XCONNECT_LIVE=1
# plus the variables each test needs (below), then:
dotnet test tests/XConnect.IntegrationTests --filter Category=Live        # read-only tests
dotnet test tests/XConnect.IntegrationTests --filter Category=LiveWrite   # tests that create content (see the write gate)
```

Test output never contains tokens, secrets or verifiers. It does contain your username, HTTP statuses and rate-limit counters.

## Variables

Set them in your shell or a secret manager, never in a file inside the repository. A missing variable makes a test **skip** with a message naming it; it never fails the run.

| Variable | Required for | Notes |
| --- | --- | --- |
| `XCONNECT_LIVE=1` | every live test | Master switch. Any other value (including `true`) keeps everything skipped. |
| `XCONNECT_CONSUMER_KEY`, `XCONNECT_CONSUMER_SECRET`, `XCONNECT_ACCESS_TOKEN`, `XCONNECT_ACCESS_TOKEN_SECRET` | OAuth 1.0a tests | From the developer portal; the app needs Read and write for the write tests. |
| `XCONNECT_BEARER_TOKEN` | app-only tests | |
| `XCONNECT_OAUTH2_CLIENT_ID` | OAuth 2.0 tests | |
| `XCONNECT_OAUTH2_CLIENT_SECRET` | confidential clients only | Omit for public clients. |
| `XCONNECT_OAUTH2_REDIRECT_URI` | PKCE steps 1 and 2 | Must exactly match the redirect registered for the app. |
| `XCONNECT_OAUTH2_PENDING_FILE` | PKCE steps 1 and 2 | A path **outside the repository**. Step 1 writes the state and verifier there (owner-only); step 2 deletes it. |
| `XCONNECT_OAUTH2_CALLBACK_URL` | PKCE step 2 | The full URL you were redirected to. Unset it to start a new authorization. |
| `XCONNECT_OAUTH2_TOKEN_FILE` | step 2 and the refresh tests | A path outside the repository; holds the access and refresh tokens (owner-only). |
| `XCONNECT_LIVE_ALLOW_TOKEN_REFRESH=1` | the refresh tests | These refresh the stored token and, if X rotates refresh tokens, replace it. Back up the token file or accept re-authorising. |
| `XCONNECT_LIVE_ALLOW_WRITES=CREATE-AND-DELETE-A-REAL-POST` | every `LiveWrite` test | **The write gate.** The exact, case-sensitive phrase. Anything else (including a trailing space) keeps write tests skipped. |
| `XCONNECT_LIVE_IMAGE_FILE` | image upload test | A small image **you** choose. The suite embeds no media. |
| `XCONNECT_LIVE_VIDEO_FILE` | chunked upload test | A small `.mp4` you choose. |

### Which gates each category needs

| Category (`--filter`) | `XCONNECT_LIVE` | Write phrase | Token-refresh opt-in | Side effects |
| --- | --- | --- | --- | --- |
| `Category=Live` (read-only) | yes | no | only the refresh tests | None, apart from rate-limit use. Refresh tests rotate the stored token. |
| `Category=LiveWrite` | yes | **yes** | no | Creates Posts and uploads media on the authenticated account. |

### Example: read-only run (OAuth 1.0a)

```shell
export XCONNECT_LIVE=1
export XCONNECT_CONSUMER_KEY=... XCONNECT_CONSUMER_SECRET=... XCONNECT_ACCESS_TOKEN=... XCONNECT_ACCESS_TOKEN_SECRET=...
dotnet test tests/XConnect.IntegrationTests --filter Category=Live
```

### Example: OAuth 2.0 PKCE, then refresh

```shell
export XCONNECT_LIVE=1 XCONNECT_OAUTH2_CLIENT_ID=... XCONNECT_OAUTH2_REDIRECT_URI=https://localhost/callback
export XCONNECT_OAUTH2_PENDING_FILE=$HOME/.xconnect/pending.json XCONNECT_OAUTH2_TOKEN_FILE=$HOME/.xconnect/token.json
dotnet test tests/XConnect.IntegrationTests --filter "FullyQualifiedName~Pkce_Step1" --logger "console;verbosity=detailed"   # prints the URL
export XCONNECT_OAUTH2_CALLBACK_URL='https://localhost/callback?state=...&code=...'
dotnet test tests/XConnect.IntegrationTests --filter "FullyQualifiedName~Pkce_Step2"
export XCONNECT_LIVE_ALLOW_TOKEN_REFRESH=1
dotnet test tests/XConnect.IntegrationTests --filter "FullyQualifiedName~TokenRefresh|FullyQualifiedName~Expired|FullyQualifiedName~ConcurrentCalls"
```

The `~/.xconnect` directory should be created with mode 700. The suite creates the files with owner-only permissions on Unix; on Windows rely on your profile's ACLs.

## What is validated

| Area | Test | Needs | Creates content? |
| --- | --- | --- | --- |
| OAuth 1.0a | `OAuth1_AuthenticatedUserLookup` | OAuth 1.0a | No |
| Rate-limit handling | `RateLimitHeaders_AreParsedFromRealResponses` (headers parsed and tracked per endpoint) | OAuth 1.0a | No |
| Error handling | `UnknownPost_IsReportedAsATypedError_NotAnException` | OAuth 1.0a | No |
| Read own Posts | `OAuth1_ReadOwnPosts` (account's own timeline; read-only; an empty timeline passes; a plan-related 403 is accepted but the output says "NOT VERIFIED") | OAuth 1.0a, app with at least Read | No |
| Unauthorized (401) | `InvalidBearerToken_IsReportedAsATypedUnauthorizedError` (sends a deliberately invalid bearer; **no real credential**; expects HTTP 401 and `XApiErrorKind.Authentication`) | only `XCONNECT_LIVE=1` | No |
| Forbidden (403) | `OtherUsersLikes_AreRefusedWithATypedClientError` (asks for another public account's liked Posts, read-only). **Assumption:** X answers 403; the test requires a typed 4xx whose kind matches the status and prints the observed status. Only an output line saying "403 mapping VERIFIED" counts as a 403 verification | OAuth 1.0a | No |
| App-only, endpoint-aware auth | `AppOnly_SearchRecent`, `EndpointAwareAuthentication_...` | Bearer (+ OAuth 1.0a) | No |
| Stream parameter name | `SampleStream_AcceptsTheTweetFieldsParameter` (the SDK sends `tweet.fields` to Post streams, as the OpenAPI document says; a 403 for plan access passes, a 400 blaming the parameter fails) | Bearer | No (reads a few events) |
| OAuth 2.0 PKCE | `Pkce_Step1_...` prints the URL; `Pkce_Step2_...` completes it, saves the token and calls the API | OAuth 2.0 app, a browser | No |
| Token refresh | `TokenRefresh_...` (forces a refresh and persists the result **first**, because X does not document refresh-token rotation), `ExpiredAccessToken_IsRefreshedTransparently_...` and `ConcurrentCallsWithAnExpiredToken_RefreshExactlyOnce` (5 simultaneous calls, asserts one refresh and one save) | Token file + refresh opt-in | Rotates the stored token |
| Post create / lookup / delete | `Post_Create_Lookup_Delete_RoundTrip` (also records how X reports a duplicate Post) | OAuth 1.0a + write gate | **Yes, deleted in a `finally`** |
| Media upload and processing | `Media_UploadImage_AttachToPost_Delete` (upload your image, alt text, Post with media, delete) | OAuth 1.0a + write gate + image file | **Yes**; the Post is deleted, the uploaded media cannot be deleted through the API and expires unattached |
| Chunked video and processing | `Media_UploadVideo_Chunked_WaitsForProcessing` | OAuth 1.0a + write gate + video file | Uploads media; no Post |

**Not covered live:** forcing a real HTTP 429 (there is no safe way to exhaust a quota on purpose; the retry and rate-limit logic is covered by deterministic unit tests, and the live suite only checks that real
headers parse). Streaming, Direct Messages, Lists, Spaces, compliance, news and the other newer modules have no live test.

## What each test leaves behind

| Test | Cleanup | Residue if it fails midway |
| --- | --- | --- |
| Read-only (`OAuth1_...`, `RateLimitHeaders_...`, `UnknownPost_...`, `AppOnly_...`, `EndpointAware...`, `SampleStream_...`) | Nothing to clean | None |
| `Pkce_Step1_...` | Writes the pending file (state and verifier, owner-only) | The pending file; delete it, it holds a one-time verifier |
| `Pkce_Step2_...` | Deletes the pending file, writes the token file (owner-only) | The token file is a credential: protect or delete it |
| `TokenRefresh_...`, `ExpiredAccessToken_...`, `ConcurrentCalls...` | Token file is rewritten with the refreshed token **before** anything else | If X rotates tokens and the process dies mid-save the old token is spent; re-run the PKCE steps |
| `Post_Create_Lookup_Delete_RoundTrip` | Deletes the Post (and the duplicate if X accepted it) in `finally` | A Post whose ID is in the failure message |
| `Media_UploadImage_AttachToPost_Delete` | Deletes the Post in `finally` | The Post (ID in the message); the uploaded media cannot be deleted through the API and expires unattached |
| `Media_UploadVideo_Chunked_WaitsForProcessing` | Creates no Post | Unattached uploaded media that expires |

Output never contains tokens, secrets, verifiers or file contents. It does contain your username, status codes, rate-limit counters, created Post/media IDs, and the authorisation URL printed by PKCE step 1 (that URL carries the `state` and PKCE *challenge*, not the verifier).

## Safety rules the suite follows

1. Read-only tests need only credentials. Anything that creates content needs the exact write phrase.
2. Created Posts carry a unique marker and are deleted in a `finally`. Deletion goes through a per-test ledger: only Post IDs this run created can be deleted, and anything else throws before a request is sent. If a delete fails the test fails with the Post ID so you can remove it by hand.
3. Secrets are never logged. The refresh test persists rotated tokens before doing anything else so a rotation cannot lock you out.
4. `LiveGateTests` always run (no network, no real environment) and prove the gates: exact phrase, master switch, refresh opt-in, empty variables count as missing, and the ledger refusal.
5. Use a test account where you can. Posts are briefly public.

## Results

The live suite has **not been run for this release**: no credentials were available when it was written. The unit suite and the conformance tests are the automated evidence; treat the live suite as the first thing to run with your own credentials.

## Recommended order for the preview-release run

Use a **test account** and an app with only the permissions you need. Each step is independent; stop at the first surprise and record it.

1. **Read-only, OAuth 1.0a:** `XCONNECT_LIVE=1`, the four OAuth 1.0a variables, `dotnet test tests/XConnect.IntegrationTests --filter "FullyQualifiedName~OAuth1_|FullyQualifiedName~RateLimitHeaders|FullyQualifiedName~UnknownPost"`.
2. **Read-only, app-only:** add `XCONNECT_BEARER_TOKEN`; run `AppOnly_SearchRecent`, `EndpointAwareAuthentication_...` and `SampleStream_AcceptsTheTweetFieldsParameter`. A 403 on search or the stream is a plan limit, not a failure.
3. **OAuth 2.0 PKCE:** the two-step flow above (verifier in the pending file, token file outside the repository).
4. **Refresh (separate authorisation):** copy the token file first; set `XCONNECT_LIVE_ALLOW_TOKEN_REFRESH=1`; run the three refresh tests. Note whether X rotated the refresh token (printed).
5. **Writes (exact phrase):** `XCONNECT_LIVE_ALLOW_WRITES=CREATE-AND-DELETE-A-REAL-POST`; run `Post_Create_Lookup_Delete_RoundTrip` first, then the image and video tests with your own files. Check the account afterwards that nothing was left behind (the failure message names any Post that could not be deleted).
6. **Report back:** what to copy into [verification-notes.md](verification-notes.md): how X reported the duplicate Post, whether refresh tokens rotate, whether `tweet.fields` was accepted, the rate-limit header values. Only then is "validated against the live API" a true statement, and only for what those tests cover.

No test covers webhooks, Account Activity, streaming connections management, compliance, Direct Messages, Lists, Spaces or news; those stay unverified live.
