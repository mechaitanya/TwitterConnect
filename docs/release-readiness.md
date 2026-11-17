# Release readiness: XConnect.Sdk 2.1.0

Assessment date 2026-11-17, repository at `main` (`765c6f4`, the merge of pull request #2; the earlier assessment was at `fc44b53`, the merge of pull request #1). Evidence below was produced by running the commands; where something was **not** run it says so.
Classification: **Blocking** (do not publish until done), **Non-blocking** (publish, disclose or schedule), **Deferred** (intentionally later).

## Verdict

The code, tests, package structure and documentation are in a publishable state, and the full CI workflow has passed on Ubuntu, macOS and Windows (GitHub-hosted) both on the pull request and on `main`. **One blocking item remains: no request has ever reached the real X API.**
Version: **`2.1.0`** (stable). `2.1.0-preview.1` was published earlier. The owner chose to release the stable version with the B4 limitation disclosed rather than fixed: no data operation has been verified against the real X API (the account returned HTTP 402 `credits-depleted`), and the README and package say so.

## Blocking

| # | Issue | Evidence | What to do |
| --- | --- | --- | --- |
| B4 | **No data operation has been verified against the real X API.** (One partial run reached X with an app-only token; data calls returned HTTP 402 `credits-depleted`.) Everything that depends on X (duplicate-Post reporting, refresh-token rotation, `tweet.fields` on streams, webhooks, compliance upload, partition limits) is unverified. | The live suite has 19 tests (all skipped by default). A first partial live run was made with an app-only bearer token (see [verification-notes.md](verification-notes.md#first-live-run-2026-11-17-xconnectsdk-210-preview1-installed-from-nugetorg)): rate-limit parsing and the invalid-token 401 were verified; **every data call returned HTTP 402 credits-depleted**, so no user or Post data retrieval has been verified. | Run the live suite with a funded test account ([integration-testing.md](integration-testing.md#recommended-order-for-the-preview-release-run)). **Disclosed, not fixed, for 2.1.0**: it must not be described as "validated". |

## Resolved

| # | Issue | How it was resolved |
| --- | --- | --- |
| B1 | Source Link would have pointed at a commit without the sources | The work was committed and merged. Build the package from a clean checkout of `main` (`fc44b53` or later), then `tools/verify_package.py` (no flags) must pass; a rehearsal in an isolated copy proved the nuspec commit, the Source Link commit and `HEAD` match. |
| B2 | README links resolved only after the push | Merged: the docs are on `main`, and all 17 README links return 200 (the old `TwitterConnect` URLs also work through GitHub's permanent redirect). |
| B3 | Windows and GitHub-hosted CI unverified | CI run on pull request #1 (`release/2.1.0-preview.1`, `1fff6c1`) and on `main` (`fc44b53`): Ubuntu, macOS and Windows each passed 20 of 20 steps. Pull request #2 (`4539afd`) passed the same three jobs, and the CI run on `main` after its merge, **run `44048951559`** (`765c6f4`), passed on Ubuntu, macOS, Windows and the informational spec-drift job. The steps include Debug and Release builds, unit and gated integration tests, samples, Native AOT, the trimmed publish, pack and package verification. |
| B3b | Clean-checkout verification of `main` | A fresh clone of `main` at `765c6f4a3e107244db6b03b7d688ab6015d26c7c` (merge of pull request #2, head `4539afd`): Debug and Release 0 warnings; 346 unit tests passed; 13 integration gate tests passed, 14 live tests skipped (before this change); `samples/Usage` built; Native AOT and trimmed publish both ran ("AOT check passed"); `dotnet pack -p:Version=2.1.0-preview.1` then `tools/verify_package.py` (no flags) and its 17 tests passed; nuspec repository `https://github.com/mechaitanya/XConnect.git` at commit `765c6f4`; Source Link raw URL returned 200; `check_package_id.py` reported the id and version free. macOS x64 only for this local run. Nothing was published. |
| (earlier) | Package ID taken; repository and project URLs; deprecated `licenseUrl`; author and copyright; relative README links; `.snupkg` not ignored; CRLF risk | `XConnect` belongs to another author, so the ID is `XConnect.Sdk` (free, checked with `tools/check_package_id.py`). The repository was renamed to `mechaitanya/XConnect`; the URLs follow it (see below). The rest was fixed in the package and repository files. |

## Status by area

| Area | Status |
| --- | --- |
| **Security** | See below. No known open issue above "non-blocking". |
| **Package metadata** | Done. ID `XConnect.Sdk`, MIT as an SPDX expression (NuGet adds the standard `licenses.nuget.org/MIT` URL itself; the deprecated `aka.ms` URL is gone), author and copyright agree (Chaitanya Krishna Jonnagaddala, as in LICENSE.txt), description with an "unofficial, not affiliated with X Corp" notice, tags, README, `projectUrl`, `repository` (type git, URL, branch, commit). |
| **Repository URL** | `https://github.com/mechaitanya/XConnect` (the repository was renamed from `TwitterConnect`; GitHub permanently redirects the old URL, which was verified). `PackageProjectUrl`, `RepositoryUrl` and the README links use the new URL. The local `origin` remote was updated to the new URL and verified (so Source Link embeds the new name when packing locally). |
| **Package icon** | None, deliberately. The only logo in history (`logo-min.png`) is the old TwitterConnect artwork; using X/Twitter branding on an unofficial SDK is inappropriate. |
| **Package contents** | One `.nupkg` and one `.snupkg`. Package: DLL, XML docs (1,499 documented members), README, LICENSE.txt, nuspec. No stray files. One dependency, `Microsoft.Extensions.Http` 11.0.0. |
| **Symbols / Source Link** | Portable PDB in the `.snupkg` with a Source Link map to `raw.githubusercontent.com/mechaitanya/<repository>/<commit>/*`, deterministic build, untracked sources embedded. Correct when the package is built from a clean commit that has been pushed; confirm after the first real pack from `main` that the raw URL for the commit resolves under the new repository name. |
| **Package verification script** | `tools/verify_package.py` checks metadata, license expression, deprecated URL, author/copyright, dependencies, stray files, README links and credentials, XML docs, the symbol package and the Source Link commit. `tools/test_verify_package.py` (16 cases) proves it passes the good package and fails on 15 deliberate breakages; it also runs in CI. `tools/check_package_id.py` checks nuget.org just before publishing. |
| **Package validation** | `EnablePackageValidation` is on with no baseline, because nothing was ever published under this ID, so there is no API baseline to compare against. 2.0 API compatibility is covered by tests (`CompatibilityAndDiTests`). |
| **Documentation** | README code compiles (all three snippets, built against the library, warnings as errors). Every relative link in README and docs resolves; external links return 200. Webhooks, backlog, verification notes, migration and integration guides are current. |
| **Build** | Debug and Release: 0 warnings, 0 errors, warnings as errors, `latest-recommended` analyzers (macOS). |
| **Tests** | 377 unit tests pass on the hardening working tree (352 on the PR #5 branch; 346 at `7382b1e`); the integration project has 15 gate tests passing and 19 live tests skipped (13 and 17 at `7382b1e`; none run against X by default). Mutation checks: removing the shared refresh lock, the connect timeout, a model field or a valid query parameter makes tests fail. |
| **AOT / trimming** | Native AOT publish and run pass on macOS x64 with no IL or trim warnings (the only warnings are Apple linker messages about .NET's own native libraries). A trimmed non-AOT publish also runs. The same publish and run steps also passed on Linux and Windows in CI. |
| **Cross-platform** | **Verified on GitHub-hosted runners** (CI run on the pull request and on `main`): Ubuntu, macOS and Windows, 20 of 20 steps each (the `latest` runner images at the time; see `ci.yml`). Before that, the same commands were run locally on macOS x64 and in the official .NET 11 Linux container. Not tested: Linux arm64, Windows arm64, and other .NET servicing versions. |
| **Live integration** | Partially run (B4): see verification-notes. Data operations unverified (402). Readiness: read-only tests run independently (`Category=Live`); writes need the exact phrase `CREATE-AND-DELETE-A-REAL-POST`; deletion is limited to Posts the test created (ledger, unit-tested); media tests need your own files; token-refresh tests need a separate `XCONNECT_LIVE_ALLOW_TOKEN_REFRESH=1`; secrets are never printed; the cleanup of every test is tabulated. |
| **Coverage** | 132 supported, 82 not implemented, 47 not supported, 6 access-restricted, 1 not applicable, of 268. Not inflated; every supported operation has a test and a call count. |

## Security fixes in this phase

- **Token refresh:** one lock per token-store instance now serialises refresh across every authenticator that shares the store (test: 6 independently built clients, 18 concurrent calls, exactly one refresh); the rotated token is saved before use and with `CancellationToken.None`, so a cancelling caller cannot lose it; the old per-instance lock is gone.
- **Streams:** `ConnectTimeout` (30 s) stops a silent server from hanging a stream forever; caller cancellation is never mistaken for it; non-positive timeouts are rejected instead of looping; reconnect backoff is jittered; a keep-alive-only server backs off instead of reconnecting in a tight loop; abandoning the stream releases the response.
- **Retries:** jittered exponential backoff (bounded by `MaxDelay`, `Jitter = 0` gives exact delays); a 429 whose reset time is in the past waits at least one second; Post creation is never retried after a 5xx or a lost connection (tested for 503, 502 and a network error).
- **Media:** simple uploads are bounded while reading (an endless stream is rejected after about 5 MB, a seekable over-limit stream without reading it); chunked uploads read exactly the declared length per segment and refuse to finalize a stream that grew; empty streams are rejected; cancellation between segments is covered.
- From the previous phase: no secret in `ToString`, dot-segment path IDs rejected, overflow-safe rate-limit parsing, RFC 5849 query decoding.

## Production hardening for 2.1.0 (H1, H2, H4)

- **H1, response size (fixed, uncommitted):** API bodies were read with `HttpCompletionOption.ResponseContentRead` plus `ReadAsStringAsync`, so `HttpClient` buffered the whole body (its own cap is 2 GB) before the SDK saw it, and stream lines came from `StreamReader.ReadLineAsync` with no limit. Now `XConnectOptions.MaxResponseBodyBytes` (16 MiB) is enforced while reading, a declared `Content-Length` over the limit is refused unread, and an oversized body throws `XResponseTooLargeException` (never truncated and parsed, not retried). `XStreamOptions.MaxLineBytes` (1 MiB) discards an oversized line as it arrives and the stream continues; the consumer gets an `XStreamEvent` with an `Errors` entry. OAuth 2.0 token responses and the legacy `XClient` use a fixed 1 MiB cap.
- **H4, pre-signed transfers (fixed, uncommitted):** `UploadIdsAsync` and `DownloadResultsAsync` had no limit of their own (under DI `HttpClient.Timeout` is infinite). `XConnectOptions.TransferTimeout` (10 minutes) bounds an upload in total, a download until it starts, and then each read of the returned stream (a stall limit). Expiry is a `TimeoutException`; caller cancellation stays an `OperationCanceledException`. Ordinary requests keep `Timeout`. The upload no longer buffers its (unused) response body.
- **H2, 401 recovery (investigated, not implemented):** see below.
- **H3 (unchanged):** refresh coordination stops at the process boundary; documented limitation for 2.1.0.

### H2 findings: no automatic recovery from HTTP 401

- Only `OAuth2UserAuthenticator` can refresh, and only from the local expiry (with a one-minute skew). OAuth 1.0a keys and app-only Bearer tokens cannot be renewed, so a 401 there means the credentials are wrong and a replay can only fail again.
- A 401 on an OAuth 2.0 call means the access token was revoked, changed or expired on X's side while the local clock said otherwise; a refresh may help, or may fail too (revoked grant, rotated refresh token spent by another process).
- Replay safety: request bodies are built by a factory per attempt (`XApiRequest.Content`), so JSON bodies can be recreated, but media segments read from a caller's stream cannot be replayed. A 401 means X rejected the request before acting on it, but X does not document that for writes, and it was not verified live (every data call returned 402).
- A forced refresh needs a guard against refresh storms (skip it when the stored token already differs from the one that failed) and runs into H3 when several processes share one rotating refresh token.
- **Recommendation:** ship 2.1.0 without it. If added later, make it opt-in, OAuth 2.0 user context only, at most one replay, idempotent requests only (never `POST`) with replayable bodies, after live verification. Until then the 401 is returned as `XApiErrorKind.Authentication`; an application can refresh with its own `XOAuth2Client` and retry reads itself.

## Non-blocking

| Issue | Note |
| --- | --- |
| Refresh coordination stops at the process boundary | Two processes sharing one refresh token can still race if X rotates tokens. Use one token per process or an externally locking store. |
| No automatic recovery from a 401 | Refresh happens from the local expiry only. |
| Assumptions awaiting live proof | `tweet.fields` accepted on streams; media segment limit; APPEND not retried; POST retried after 429; chunked pre-signed upload. Listed in [verification-notes.md](verification-notes.md). |
| Package is unsigned | NuGet author signing needs a certificate. |
| `docs/` links in the README point at `main` | They follow the default branch, not the release tag; pin to a tag after tagging if you want them frozen. |

## Deferred

All unimplemented API areas, with reasons, in [backlog.md](backlog.md): Activity API (next), Community Notes, DM media download, developer account, articles, compliance token routes, Media library and Amplify (docs unconfirmed), Livestream API (separate product), the Enterprise streams, and X Chat (deliberately unsupported).

## Recommended version

**`2.1.0`** is the stable number for this ID: it is the first stable release under `XConnect.Sdk`, the 2.0 types (`XClient`, `XCredentials`, ...) are preserved, and the API is additive. The project file carries `2.1.0`; a preview is produced only by passing `-p:Version=2.1.0-preview.N` to `dotnet pack`. Publishing is a separate, explicitly authorized step (see below).

## Release checklist

Written for the preview and valid for the stable release too: use `2.1.0` wherever it says `2.1.0-preview.1` (no `-p:Version` override is needed for the stable package).

Done: commit, push, pull request #1, CI on three operating systems, merge to `main`, repository rename and URL update (the follow-up pull request carries the URL change, the agent rules and the verifier check).

Remaining, all yours:

1. **Merge the follow-up change** (repository URL, rules files, this document) and confirm CI is green on `main` again.
2. **Sync a clean checkout of `main`:** `git switch main && git pull --ff-only`. `origin` already points at `https://github.com/mechaitanya/XConnect.git`; `tools/verify_package.py` fails a package whose Source Link repository differs from its nuspec repository, so a stale remote cannot slip through.
3. **Live run:** follow [the order in integration-testing.md](integration-testing.md#recommended-order-for-the-preview-release-run) with a test account; record the results in [verification-notes.md](verification-notes.md). Decide whether to publish the preview before or after it.
4. **Check the ID:** `python3 tools/check_package_id.py XConnect.Sdk 2.1.0-preview.1` (must print OK).
5. **Pack from the clean `main`:** `dotnet pack src/XConnect -c Release -p:Version=2.1.0-preview.1 -o artifacts`
6. **Verify (no flags; must pass):** `python3 tools/verify_package.py artifacts && python3 tools/test_verify_package.py artifacts`
7. **Publish** with the manual workflow (needs the one-time [Trusted Publishing setup](#trusted-publishing-setup)): Actions, `nuget-publish`, Run workflow, branch `main`, version `2.1.0-preview.1`, then approve the `nuget` environment. The workflow repeats steps 4 to 6 on the exact tip of `main` and pushes only the package it verified. Running it is the publication; only do it when you have decided to publish.
   (Manual alternative, still yours alone: `dotnet nuget push artifacts/XConnect.Sdk.2.1.0-preview.1.nupkg --api-key <your key> --source https://api.nuget.org/v3/index.json`; the `.snupkg` beside it is uploaded to the symbol server too.)
8. **Check the result:** install the package into a scratch project and run one call; open the package page and the Source Link.
9. **For `2.1.0`:** rerun steps 4 to 8 with `-p:Version=2.1.0` once the live results are in.

## Trusted Publishing setup

`.github/workflows/nuget-publish.yml` publishes through NuGet Trusted Publishing, so no long-lived API key exists. Writing the workflow published nothing, and it has not been run. Setup that only you can do:

1. **nuget.org policy** (Account, Trusted Publishing): owner `mechaitanya`, repository `XConnect`, workflow file `nuget-publish.yml`, environment `nuget`, scope Push. Choose "new packages and package versions" for the first release (the id does not exist yet), then tighten it to "only new package versions".
2. **Repository variable** `NUGET_USER` (Settings, Secrets and variables, Actions, Variables): your nuget.org **profile name** (not your email). It is not a secret.
3. **GitHub environment `nuget`** (Settings, Environments): add yourself as a **required reviewer**, enable "Prevent self-review" only if a second reviewer exists (otherwise leave it off, or you cannot approve your own run), and restrict **deployment branches to `main`**. No secrets are needed in it.
4. **Optional:** after the first release, set the policy to new versions only, and review who has write access, since workflow runs need write access.

What the workflow enforces: manual trigger only; `main` only, and only the current tip of `main`; the version is validated as SemVer; green CI for the commit on all three systems; the package id and version are checked against nuget.org (an existing version fails); Release build with warnings as errors, unit tests and gated integration tests; `tools/verify_package.py` with no bypass flags and its own tests; nuspec id, version, repository and commit checked; the publish job runs only after the build job and the environment approval, and pushes the verified `.nupkg` without `--skip-duplicate`, so a duplicate fails instead of being skipped. The `.nupkg` and `.snupkg` are kept as a build artifact for 30 days.
