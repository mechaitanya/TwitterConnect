# Coverage backlog

Authoritative source: the generated [coverage matrix](api-coverage.md) (OpenAPI v2.175, 268 operations). This page groups every operation the SDK does **not** yet implement and says
why, what it depends on and what it would take. Facts are labelled: **Spec** (the OpenAPI document), **Docs** (a docs.x.com page read on 2026-11-17) or **Assumption**.

| Status | Operations |
| --- | --- |
| Supported | 132 |
| Not implemented | 82 |
| Not supported (deliberate) | 47 |
| Access-restricted | 6 |
| Not applicable | 1 |

## Done in this phase (20 operations)

Chosen because they complete modules that were already partly implemented, or because their request/response shapes are small and fully specified.

| Group | Operations | Notes |
| --- | --- | --- |
| Users | `GET /2/users/me/followers`, `GET /2/users/me/following` | Same data as the `{id}` forms without the lookup. User context (OAuth 1.0a or 2.0). |
| Spaces | `GET /2/spaces/{id}/buyers` | **Spec:** OAuth 2.0 user context only. |
| Streaming connections | `GET /2/connections`, `DELETE /2/connections`, `/all`, `/{endpoint_id}` | App-only. Destructive for every consumer of a stream, so `endpoint_id` is checked against the spec's closed list before sending. |
| Webhooks | `GET/POST /2/webhooks`, `PUT/DELETE /2/webhooks/{id}`, `POST /2/webhooks/replay` | Management calls only. `XWebhookSecurity` implements the receiving-side CRC and signature scheme (**Docs**). |
| Webhook to filtered stream | `GET /2/tweets/search/webhooks`, `POST/DELETE /2/tweets/search/webhooks/{id}` | App-only. |
| Account Activity | five `/2/account_activity/...` operations | Subscriptions; events arrive at your own endpoint. |

The earlier conformance note called all webhook operations "outside a client SDK". That was wrong for the management endpoints (ordinary REST calls) and only true for receiving events.

## Remaining, grouped

Ranking uses: **Demand** (how many developers need it), **Deps** (what must exist first), **Auth** (complexity), **Effort**, **Access** (who can call it), **Security** (what a mistake costs). Recommendation order is top to bottom.

### 1. Activity API: 6 operations. Recommended next.

`GET /2/activity/stream`, `GET/POST/DELETE /2/activity/subscriptions`, `PUT/DELETE /2/activity/subscriptions/{id}`.

- **Demand:** high. It is the current way to receive account events without hosting a webhook.
- **Docs:** part of the X API, not a separate product. Self-serve accounts get 1,500 subscriptions (Enterprise 75,000, Partner 150,000). Some events are Enterprise-only (for example `news.new`).
- **Deps:** the existing streaming infrastructure (keep-alive timeout, reconnect backoff, no buffering) fits the event stream. New typed models for event payloads are needed, and the payloads differ per `event_type`.
- **Auth:** app-only or user context. **Spec:** with a user token the scope must match the event family (`dm.read` for chat/dm events, `like.read`, `mute.read`, `block.read`, and so on), and `user_id` must identify the authenticated user.
- **Effort:** medium. **Security:** low. **Unknown:** the event payload schemas should be read from the spec before modelling; do not guess them.

### 2. Direct Message media download: 1 operation

`GET /2/dm_conversations/media/{dm_id}/{media_id}/{resource_id}`. **Spec:** OAuth 2.0, `dm.read`. Completes the DM module. Returns binary content, so it needs a streaming response like compliance download. Effort low, security: it hands private media to the caller, so never log URLs with ids.

### 3. Community Notes: 5 operations

`POST /2/notes`, `DELETE /2/notes/{id}`, `POST /2/notes/evaluate`, `GET /2/notes/search/notes_written`, `GET /2/notes/search/posts_eligible_for_notes`.

- **Docs:** part of X API v2, aimed at AI Note Writers. **Spec:** `POST /2/notes` has a `test_mode` flag that makes a note invisible to the public.
- **Security:** creating a note is a public write. An SDK default should be `test_mode = true`, and the live suite must never create notes without the write phrase.
- **Effort:** low to medium. **Assumption:** that your developer account is registered as a Note Writer; the docs, not the spec, describe that.

### 4. Developer account: 2 operations

`GET /2/account`, `POST /2/account` (**Spec:** OAuth 2.0, `developer.read` / `developer.write`). The POST creates a billed developer account for the authenticated user (idempotent). **Security:** a billing side effect, so implement `GET` first and gate `POST` behind an explicit method name. Low demand.

### 5. Articles: 2 operations

`POST /2/articles/draft`, `POST /2/articles/{id}/publish`. Low demand; publishing is a public write. The request schema should be read before estimating effort.

### 6. Compliance routes with a `token` query parameter: 2 operations

`GET /2/compliance/jobs/{id}/download`, `PUT /2/compliance/jobs/{id}/upload`. The SDK uses the job's pre-signed `upload_url` / `download_url` (documented flow). Leave unimplemented unless X documents the token routes as a supported path. Pre-signed URL requests intentionally carry **no** Authorization header.

### 7. Media library and Amplify: 8 operations

`/2/media/library*`, `/2/media/amplify*` (**Spec:** `media.library.read/write`). **Docs:** the expected introduction pages return 404, so product, licensing and availability cannot be confirmed. Do not implement as standard v2 until X documents them.

### 8. Livestream API (broadcasts, sources, calls, bots, region): 56 operations. Do not implement as standard v2.

`/2/broadcasts*`, `/2/sources*`, `/2/calls*`, `/2/bots*`, `/2/region`, `/2/users/{id}/broadcasts*`, `/2/users/{id}/sources*`.

- **Docs:** the docs navigation lists **Livestream API** as its own product, separate from X API (and from Enterprise). Scopes are `broadcast.*` and `calls.*`; bot endpoints use app-only tokens. The spec exposes the operations but says nothing about licensing.
- **Decision:** treated as separately licensed. It is also the largest group (more than a fifth of the spec) with media-streaming semantics (stream keys, RTMP sources, call join/leave) that a REST SDK cannot validate without access. Revisit as a separate package or module when access is available to test.

### 9. Access-restricted streams: 6 operations

Enterprise compliance, label and likes streams (`/2/tweets/compliance/stream`, `/2/users/compliance/stream`, `/2/tweets/label/stream`, `/2/likes/compliance/stream`, `/2/likes/firehose/stream`, `/2/likes/sample10/stream`). Partitioned, different payloads, Enterprise enrolment. Not implemented; the connection management endpoints (done) already accept their endpoint ids.

### 10. X Chat: 44 + 3 operations. Deliberately not supported.

`/2/chat/*` and the three `public_keys` endpoints (**Spec:** "keys registered for X Chat encryption"). End-to-end encrypted chat needs client-side key generation, storage and cryptography that a thin REST SDK should not half-implement. Unencrypted Direct Messages (`/2/dm_*`) are supported.

## Dependency graph

```
Activity API ──► streaming infrastructure (exists) ──► event models (read spec first)
Community Notes ──► Post lookup (exists); live test needs write gate
DM media download ──► DM module (exists), streaming response helper (exists in compliance)
Developer account GET ──► nothing; POST ──► explicit billing-side-effect gate
Livestream / Media library / Amplify ──► product access; nothing else is blocked by them
X Chat ──► client-side crypto library decisions (out of scope)
```

## What would change these decisions

- X publishing access terms for Livestream, Media library or Amplify.
- A real account with Activity API access, so event payloads can be validated live.
- A decision on whether the SDK should ever hold encryption keys (X Chat).
