# XConnect engineering rules

Authoritative for every agent and contributor (`CLAUDE.md` and `.github/copilot-instructions.md` point here). Details live in `docs/`; this file holds only the rules. If a rule is wrong or outdated, say so and ask: do not quietly work around it.

## 1. Project identity and standards

- NuGet package **`XConnect.Sdk`** (the id `XConnect` belongs to another author on nuget.org). Assembly and namespaces are `XConnect`. MIT licensed. An unofficial X API v2 SDK; never imply affiliation with X Corp.
- Target `net11.0`, C# 15, nullable on, implicit usings, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended` (all in `Directory.Build.props`). A build with any warning is a failed build.
- The library has **one** runtime dependency, `Microsoft.Extensions.Http`. Do not add another package without approval. No Newtonsoft.Json.
- Test stack: xUnit and **AwesomeAssertions** (not FluentAssertions 8+, which is commercially licensed).
- Layout: `src/XConnect` (`Clients`, `Models`, `Authentication`, `Infrastructure`, `Streaming`, `Serialization`, `Extensions`), `tests/XConnect.Tests`, `tests/XConnect.IntegrationTests`, `samples/Usage` (compiled doc snippets), `samples/AotCheck`, `tools/`, `docs/`.
- The repository is `github.com/mechaitanya/XConnect` (renamed from `TwitterConnect`; GitHub redirects the old URL). `PackageProjectUrl`, `RepositoryUrl` and the README links follow it. The local `origin` remote points at it too. Do not change remotes, or these URLs, without approval.

## 2. Git safety (mandatory)

- Run `git status` before modifying files, and treat every existing staged, unstaged and untracked change as the user's.
- **Never** commit, push, merge, rebase, reset, clean, checkout over changes, stash-drop, force-push or publish without explicit approval **for that action**. Approval for one step (for example a commit) does not cover the next (the push).
- Never discard or overwrite existing user changes. Before deleting or overwriting a file, look at it.
- Never `git add -A` or `git add .` blindly. Stage named paths you have reviewed.
- `main` (and `release/**` branches and tags) are protected by GitHub rulesets: changes reach `main` only through a pull request whose `ubuntu-latest`, `macos-latest` and `windows-latest` CI jobs pass, and deleting or force-pushing those branches is blocked. Only the repository owner can bypass this, which includes any agent acting with the owner's GitHub token, so the approval rules in this section are what actually constrain an agent. Do not weaken or edit the rulesets without approval.
- Do not create, switch, rename or delete branches, and do not add or change remotes, without approval. Do not modify `main`.
- Review the final diff (`git diff`, and `git diff --no-index /dev/null <file>` for untracked files) before declaring work complete.
- Commit messages and PR text are plain: no `Co-Authored-By` or other trailers, no "generated with" lines.
- Never put credentials in the repository, commit messages, logs or test output. Secrets come from environment variables only.

## 3. X API v2 coverage accuracy

- `docs/api-coverage.md` and `docs/spec-snapshot.json` are **generated** by `tools/generate_coverage.py` from the official OpenAPI document. Never edit them by hand. After adding or changing an operation, update the member table in the generator, regenerate, and run the tests.
- Never claim coverage the matrix does not show. State numbers from the matrix, not from memory, and never say "full coverage" while any row is not `Supported`.
- An operation counts as supported only if it is implemented, has authentication that is a subset of what the spec allows, and has at least one test that calls it.
- Research the spec and the official docs before implementing. Mark every assumption as an assumption (in code docs and `docs/verification-notes.md`); never implement speculative X behaviour.
- Do not implement separately licensed or access-gated products as if they were standard v2 (Livestream API, X Chat, Media library and Amplify are currently not implemented; see `docs/backlog.md`).

## 4. Public API and backward compatibility

- The 2.0 types `IXClient`, `XClient`, `XCredentials`, `XPostResult`, `XRateLimit` keep their public shape and behaviour (`CompatibilityAndDiTests`).
- Changes are additive. Never remove, rename or retype a public member, and never reorder or renumber enum values (new members are appended, as `XApiErrorKind.Conflict` was).
- Models are init-only records with nullable properties named after the spec's snake_case fields. Add a typed property when the spec defines a shape and `JsonElement` when the spec leaves it undefined; never guess a shape.
- Every public type and member needs an XML doc comment (CS1591 is an error). Document access restrictions and undocumented-behaviour assumptions there.
- The client interfaces (`IXConnectClient`, `IXPostsClient`, ...) are for consumers; adding members to them is allowed but must be noted in `docs/migration.md`.
- HTTP failures are returned as `XResponse<T>` with a typed `XApiError`; exceptions are for programmer errors (`ArgumentException`) and for non-recoverable states, as they are today.

## 5. Authentication and security

- Each request declares the `XAuthMethods` the spec allows; the transport picks the first configured authenticator that fits and throws `InvalidOperationException` before sending otherwise. `SpecConformanceTests` rejects any route whose auth is wider than the spec.
- Never log or format a token, secret, verifier, `Authorization` header, signature or request body. Types that hold secrets override `ToString` to mask them (`XCredentials`, `XOAuth2Token`, `XOAuth2Options`); do the same for any new one. Logging uses route templates, not URLs.
- OAuth 2.0 refresh: one lock per token-store instance; persist the rotated token **before** use and with `CancellationToken.None`; never let caller cancellation lose a rotated token. The lock does not span processes: say so wherever relevant.
- Pre-signed URLs (compliance upload and download) must be HTTPS and get **no** `Authorization` header.
- Webhook signatures and CRC tokens: HMAC-SHA256 with the documented secrets and constant-time comparison (`XWebhookSecurity`). Verify raw bytes before parsing.
- Validate caller-supplied path parameters and reject values that could alter the route (`.`, `..`, blank). State-changing or destructive operations get explicit method names and local validation.

## 6. HTTP pipeline, retries and rate limiting

- Every API call goes through `Infrastructure/XApiTransport.cs`; no client builds its own `HttpClient` calls.
- Retries are bounded and safe: idempotent methods retry on 429, 5xx and network errors; `POST` retries **only** on 429. **Never retry Post creation (or any POST) after an ambiguous failure.** Retry waits honour `Retry-After`, then `x-rate-limit-reset` (with a one-second margin and a one-second floor), then jittered exponential backoff capped by `MaxDelay`. Never wait indefinitely.
- Use `TimeProvider` for every clock, timer and delay so tests stay deterministic. Cancellation must propagate through retries, polling, uploads and streams.
- The rate-limit tracker is keyed by `METHOD route-template`, keeps the newest window and the lowest `Remaining`, and never throttles by default.
- Streams are read line by line and never buffered whole; they use `ConnectTimeout`, `KeepAliveTimeout`, jittered backoff and a `MaxReconnects` bound, and dispose the response when abandoned.

## 7. System.Text.Json and Native AOT

- All serialization goes through the single source-generated `Serialization/XConnectJsonContext.cs`. Register every new wire type there (`[JsonSerializable]`), including request-body DTOs.
- No reflection-based serialization, `Activator`, `Type.GetType`, dynamic code or `Enum` reflection in `src`. Computed helper properties on models are `[JsonIgnore]`.
- The library stays `IsAotCompatible` with zero trim or AOT warnings. When adding wire types, extend `samples/AotCheck` and run both the Native AOT publish and the trimmed publish (see section 12).

## 8. Testing

- **Unit tests** (`tests/XConnect.Tests`) are fully stubbed: no network, no credentials, no sleeping. Use `QueueHandler`, `TestClients` and `InstantTimeProvider`.
- `QueueHandler` audits every request against the OpenAPI contract snapshot (parameters, types, enums, ranges, body shape, required fields). Fix the code or the test data; do not disable the audit to make a test pass.
- Every new operation needs tests for the happy path, its authentication selection, argument validation and its error mapping; every bug fix needs a regression test. Prove important new checks fail when the code is broken (mutation check) and restore the code afterwards.
- **Integration tests** (`tests/XConnect.IntegrationTests`) are opt-in and skipped by default. Gates: `XCONNECT_LIVE=1`; writes also need `XCONNECT_LIVE_ALLOW_WRITES=CREATE-AND-DELETE-A-REAL-POST` (exact); token-refresh tests also need `XCONNECT_LIVE_ALLOW_TOKEN_REFRESH=1`. Deletion is limited to Posts the same test created (`Live.PostLedger`). Media comes only from user-supplied files. See `docs/integration-testing.md`.
- **Live API:** never make a real X request without credentials **and** explicit authorization, and never create or delete real Posts without the write gate. Never claim live validation unless a real request was made, and then only for what that test covers.

## 9. Performance and memory

- Bound everything that reads untrusted or large input: enforce size limits while reading, not after (`MediaClient`), cap segment buffers, never `ReadAsStringAsync` a stream.
- Chunked media uploads hold at most one segment in memory and require the declared length to match.
- Pagination is bounded (`MaxPages`, repeated-token guard); never loop on a token without a termination guarantee.
- Avoid per-call allocations on hot paths that the source-generated context already avoids; do not add caching or concurrency features speculatively.

## 10. Documentation

- Every behaviour change updates the relevant guide in `docs/` and `README.md` in the same change. Do not duplicate architecture or release content into this file.
- README and doc code snippets must compile: add them to `samples/Usage`, and check README blocks before release. The packed README uses absolute `https://github.com/...` links only; docs may use relative links.
- State limits and unverified assumptions plainly (`docs/verification-notes.md`). Never describe a feature that is not implemented as available.
- Generated documents (`docs/api-coverage.md`, `docs/spec-snapshot.json`) are regenerated, not edited.

## 11. NuGet packaging and release management

- Package id `XConnect.Sdk`; version lives in `src/XConnect/XConnect.csproj` (`2.1.0`); previews are produced with `-p:Version=2.1.0-preview.N`, never by editing the file for one build.
- Before any release: `python3 tools/check_package_id.py XConnect.Sdk <version>`, pack from a clean, pushed commit (Source Link embeds the commit), then `python3 tools/verify_package.py <dir>` **without** `--allow-dirty-commit`, and `python3 tools/test_verify_package.py <dir>`. A package is not release-ready merely because `dotnet pack` succeeded.
- Do not add a package icon that carries X or Twitter branding. Keep the single runtime dependency.
- **Never publish to NuGet.** This stays the default. The only permitted route is the dedicated, manually triggered workflow `.github/workflows/nuget-publish.yml` (NuGet Trusted Publishing; no API key is stored), run from `main`, after the `nuget` environment's required approval **and** the owner's explicit authorization for that release. Creating, changing or merging the workflow does not authorize a publication; never run it, push a package by any other means, or create a release tag without that authorization. The release checklist is `docs/release-readiness.md`.

## 12. Engineering workflow and reporting

- Quality gates before calling work done (run them; do not assume): `dotnet build XConnect.sln -c Debug` and `-c Release` (0 warnings), `dotnet test XConnect.sln -c Release`, regenerate the matrix (`python3 tools/generate_coverage.py`) and check it is unchanged or explained, build `samples/Usage`, `dotnet publish samples/AotCheck -c Release` and run the result, the same with `--use-current-runtime --self-contained -p:PublishAot=false -p:PublishTrimmed=true`, then `dotnet pack` and `tools/verify_package.py` when packaging is touched.
- Report outcomes faithfully: what was run, what was not, what failed, and what is an assumption. Name platforms and environments that were not exercised (Windows and GitHub-hosted CI are examples) instead of implying success.
- Prefer finishing the current phase over widening scope; do not add features, files or abstractions that were not asked for.
- When the right choice depends on the owner (naming, licensing, anything outward-facing), ask once with a recommendation instead of guessing.
