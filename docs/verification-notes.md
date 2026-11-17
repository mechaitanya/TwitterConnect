# Verification notes: what X documents, and what the SDK does about it

Research performed on 2026-11-17 against the official documentation and the OpenAPI document (v2.175). "Not stated" means the page was read and does not say; it is not evidence either way.
Where X is silent the SDK takes the conservative behaviour and says so in the XML docs.

| Question | What the documentation says | What the SDK does |
| --- | --- | --- |
| Is `x-transaction-id` a documented response header? | **No.** The rate-limits and error pages describe only `x-rate-limit-limit`, `x-rate-limit-remaining` and `x-rate-limit-reset`. The OpenAPI document defines no response headers at all. | Still read when present (it is useful in support requests) but surfaced as `RequestId` with an explicit "undocumented, best-effort" note. Nothing depends on it. |
| How is a duplicate Post reported? | **Not documented.** The errors page lists no duplicate-Post problem type (its `duplicate-rules` type is for stream rules). | `XApiErrorKind.DuplicatePost` is a **heuristic** (HTTP 403 whose detail mentions "duplicate") and is documented as such. Callers must not treat it as a contract; `Authorization` with the raw `Detail` is always available. |
| Error taxonomy | Documented: HTTP 200/201/204, 400, 401, 403, 404, 409 (filtered stream only), 429, 500/502/503/504; problem `type` values `invalid-request`, `resource-not-found`, `not-authorized-for-resource`, `client-forbidden`, `usage-capped`, `rate-limit-exceeded`, `streaming-connection`, `rule-cap`, `invalid-rules`, `duplicate-rules`, `about:blank`. Some 200 responses carry `data` and `errors`. | The documented `type` is consulted first, then the status. Added `XApiErrorKind.Conflict` (409, appended so existing enum values are unchanged). 204 is success. Partial errors on 200 are kept in `PartialErrors`. |
| OAuth 2.0 refresh-token rotation | **Not stated.** The page says the access token lasts two hours unless `offline.access` is used, that a refresh token is issued with that scope, and gives the refresh request; it does not say whether the refresh token rotates, how long it lives, or how reuse behaves. | Defensive: a rotated refresh token is adopted when returned and the old one kept when not; the new token is persisted before use; concurrent refreshes are serialised (and shared across DI-injected clients) so a single-use token cannot be spent twice. If X invalidates the old token on a failed save, the app is signed out: implement `IXTokenStore.SaveTokenAsync` durably. |
| Is retrying a media `APPEND` safe? | **Not established.** The chunked-upload page documents INIT, APPEND (`segment_index` from 0, at most 5 MB recommended, 8 MB server maximum), FINALIZE and STATUS, but not retry semantics. (The media best-practices page returned 404.) | Uploads are not retried automatically (other than after a 429, which X rejects before processing). |
| Rate limits for concurrent requests | **Not stated.** Limits are per endpoint, per user (user tokens) or per app (Bearer); only stream connection counts are described (filtered stream: 1 connection). | New: the tracker keeps the newest window and the lowest `Remaining` per endpoint, so out-of-order responses cannot make quota look larger. New: optional `XConnectOptions.MaxConcurrentRequests`. The SDK does not throttle by default. |
| Media metadata | OpenAPI: `alt_text` (max 1000 characters), `allow_download_status`, `content_expiration`, `domain_restrictions`, `found_media_origin`, `management_info`, `shared_info`, `upload_source`; plus untyped `audience_policy`, `geo_restrictions`, `sensitive_media_warning`, `sticker_info`, `preview_image`. Subtitles: `POST`/`DELETE /2/media/subtitles` with categories `TweetVideo`/`AmplifyVideo`; subtitle files are uploaded with category `subtitles` (1 MB). | Typed support for the eight documented properties with the 1000-character check; subtitles create/delete; `Subtitles` upload category (`text/srt`, `text/vtt`, 1 MiB). Untyped properties are not exposed rather than guessed. |
| Post fields, `call_to_actions` | OpenAPI: `media.call_to_actions` with `app_install` (`app_store_id`, `ipad_app_store_id`, `play_store_id`), `visit_site` (`url`) or `watch_now` (`url`); `media.title`, `description`, `embeddable`, `preview_media_id`. `reply.auto_populate_reply_metadata` is accepted but dropped. Docs: text is required unless media is provided. | All modelled (one CTA variant, HTTPS URL, validated locally). `auto_populate_reply_metadata` is deliberately omitted. Poll-only Posts without text are now rejected locally, as the documentation requires text or media. |
| Analytics metrics | OpenAPI lists 20 Post metrics plus `timestamped_metrics` (buckets with `timestamp` and `metrics`), and 10 video metrics. Video counters are an integer *or* the original string. | The earlier model covered 10 Post metrics and no buckets; now all are modelled, and video counters are `JsonElement`. |
| Webhook CRC and signatures | Docs: the CRC `response_token` and the request signature are `sha256=` plus the Base64 HMAC-SHA256 of the `crc_token` / the **raw request body**. Key: the app's **OAuth 2.0 client secret** (recommended; header `X-Twitter-Webhooks-Signature-OAuth2`) or its **OAuth 1.0 consumer secret** (legacy; header `X-Twitter-Webhooks-Signature`). The bearer token is never the key. Webhooks must be public HTTPS without a port and answer within 10 seconds. | `XWebhookSecurity` implements exactly that, with a constant-time comparison. Registration rejects non-HTTPS URLs and URLs with a port before sending. Not exercised against X. |
| Replay window format | **Spec:** `from_date` / `to_date` are UTC strings in `yyyymmddhhmm` format; the docs say events from up to the past 24 hours can be replayed. | The SDK formats UTC timestamps that way and rejects an empty or reversed window. It does not enforce the 24-hour limit. |
| Query parameter names on Post streams | **Spec:** every Post stream (`sample`, `sample10`, `search/stream`, `firehose` and its language variants) lists `tweet.fields`; every other operation lists `post.fields`. | **Fixed:** the SDK sent `post.fields` to streams. It now sends `tweet.fields` there (the documented name). **Assumption:** the server accepts the documented name; verify live. |
| `partition` on partitioned streams | **Spec:** required, minimum 1; maximum 2 for `sample10` and the Japanese/Korean/Portuguese firehoses, 8 for English, 20 for the full firehose. | **Fixed:** the SDK accepted 0. It now rejects values below 1 locally. The maximum is not enforced, because X may raise it. |
| Compliance operations | Documented: create, get, list, **cancel** (`DELETE`, for a job awaiting upload or processing), upload IDs with `PUT` to `upload_url`, download results from `download_url`. Result format: only `action` and `reason` are described. Enterprise requirement: not stated. | Added cancel, `UploadIdsAsync` and `DownloadResultsAsync`. The pre-signed URLs get **no credentials** (and must be HTTPS), so our tokens never reach another host. The OpenAPI document also has `/upload` and `/download` routes taking a `token` query parameter; those are not used. This flow follows the docs but has not been exercised live. |

## Things found by the audit that the first release got wrong

- `GET /2/tweets/counts/recent` was reachable with OAuth 1.0a, which the specification does not allow. Fixed in the previous release; now enforced by `SpecConformanceTests`.
- A Post with only a poll and no text passed local validation, contrary to the documented "text required unless media". Fixed.
- The coverage matrix claimed every supported row was tested; it is now generated and shows the call count per operation.
- Typed `HttpClient` registrations are transient, so injected clients each had their own OAuth 2.0 refresh lock and rate-limit tracker. Fixed with shared state (previous release); regression test added.

## Found by the contract audit in this phase

The request audit and the model comparison (see [Conformance](#how-conformance-is-checked)) found these in code that previously passed all tests:

- Post streams sent `post.fields` where the specification documents `tweet.fields` (fixed, regression test added).
- Partitioned streams accepted partition 0 (fixed).
- Response models silently dropped **616 documented fields**: 22 on Post, 15 on User, 5 on Media, plus Space, Direct Message and upload fields. Typed fields were added where the specification defines a shape and `JsonElement` where it leaves the object undefined. One documented field is still not modelled (`associated_metadata` on the media metadata result: an untyped object).
- `XMediaProcessingInfo.IsSucceeded` and `IsFailed` are computed helpers but would have been written as JSON fields; they are now `[JsonIgnore]`.
- Several unit tests used values the specification forbids (partition 3 on a stream whose maximum is 2, `max_results=2` where the minimum is 5, a `dm_event.fields` value that does not exist). They now use valid values, which is what the audit enforces.

## How conformance is checked

`docs/spec-snapshot.json` (generated, 270 KB) keeps, for every operation the SDK implements, its query parameters (types, enums, ranges, required), JSON request schema and success response schema, with the component schemas they reach.

1. **Routes, methods and authentication**: the code must call only operations that exist, with an authentication subset of what the specification allows (`SpecConformanceTests`).
2. **Request audit**: every request any unit test sends through `QueueHandler` is validated against the contract: unknown or missing query parameters, integer ranges, enum values, array sizes, JSON body keys, types and required fields. A violation throws, so the calling test fails. This covers only requests the tests make.
3. **Response models**: for 123 operations, every public model property must exist in the documented response schema, and every field the specification marks as required must be modelled. Models shared by operations with different documented responses are listed explicitly with a reason.
4. **Scopes and pagination**: every scope a supported operation documents has an `XScopes` constant; every paged operation has a token parameter to send back.

Limits: path parameter patterns are not checked (tests use short fake IDs), multipart and form bodies are not validated, string `format` and `pattern` are not checked, and the model comparison is by property name, not by exact type.

## Independent review (read-only, by a separate reviewer)

Fixed, each with a regression test (`ReviewFindingsTests`): the rotated refresh token is now saved with `CancellationToken.None` so a cancelling caller cannot lose it; `XOAuth2Options.ToString()` no longer prints the client
secret; a 429 whose reset time is in the past (clock skew) waits at least one second; path IDs of `.` or `..` are rejected; rate-limit headers that overflow `int` are ignored instead of wrapped; `+` in a signed query string
is decoded as a space (RFC 5849); an absurd `expires_in` is clamped; replay windows that collapse to the same minute are rejected.

**Known limitations, not fixed** (decide before relying on them):

- **Refresh across processes.** The refresh lock is per token-store instance within one process. Separate processes (or stores that merely read the same record) can still refresh concurrently, and if X rotates refresh tokens the second attempt fails.
- **No 401 recovery.** An access token is refreshed from its local expiry only. A revoked token, or one without `expires_in`, surfaces the 401.
- **Pre-signed transfers have no timeout** of their own (stream connection now has `ConnectTimeout`). Under DI `HttpClient.Timeout` is infinite; pass a `CancellationToken` with a timeout.
- **Buffering.** Non-stream response bodies are read whole; streamed lines have no maximum length.
- **Assumptions to verify live:** the local limit of 10,000 media segments may exceed X's documented segment index range; APPEND is not retried after a 5xx or network error (safe, no resume); POST retries after a 429 assume X rejects before processing; compliance upload sends chunked encoding for a non-seekable stream, which some pre-signed storage URLs reject.

Addressed since the review (with tests): a stream connection can no longer hang forever (`ConnectTimeout`); reconnect and retry delays are jittered; a keep-alive-only server no longer causes a tight reconnect loop; refreshes are coordinated across authenticators sharing a store; simple media uploads are bounded while reading; a chunked upload cannot finalize a stream that changed length.
