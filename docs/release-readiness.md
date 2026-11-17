# Release readiness: XConnect.Sdk 2.1.0

Assessment date 2026-11-17, repository at `main` (`1982ccf`) with uncommitted work. Evidence below was produced by running the commands; where something was **not** run it says so.
Classification: **Blocking** (do not publish until done), **Non-blocking** (publish, disclose or schedule), **Deferred** (intentionally later).

## Verdict

The code, tests, package structure and documentation are in a publishable state **on macOS**. Four blocking items remain, and all four are actions only the repository owner can take (commit and push, run CI once, run the live suite).
Recommended version: **`2.1.0-preview.1`** first, then `2.1.0` once the live suite and CI have passed (see the end).

## Blocking (all four need you)

| # | Issue | Evidence | What to do |
| --- | --- | --- | --- |
| B1 | **Nothing is committed**, so Source Link would point at `1982ccf`, which contains none of the new sources. | `tools/verify_package.py` fails on a dirty tree. A rehearsal in an isolated copy (commit, pack, verify with no flags) passes, and the nuspec commit, the Source Link commit and `HEAD` are identical, so the flow works once you commit. | Commit on a release branch (see the checklist), rebuild from that clean commit, verify with no flags. |
| B2 | **README and docs links resolve only after the push.** The packed README links to `.../TwitterConnect/blob/main/docs/...`; the remote `main` still has only the original 7 files. | All 17 doc links map to existing local files; the remote lacks them. Remote `main` = local `main` = `1982ccf`, so the push is a fast-forward. | Merge the release branch to `main` (or point the links at the branch), then open the README links. |
| B3 | **Windows is unverified, and GitHub-hosted runs have not happened.** | See "Cross-platform" below: macOS and Linux x64 passed by running the workflow's own commands; the workflow itself has never run on GitHub; Windows was not run at all. | Push the branch and watch the first CI run. |
| B4 | **No request has ever reached the real X API.** | All 14 live tests are skipped. | Run the live suite ([integration-testing.md](integration-testing.md#recommended-order-for-the-preview-release-run)). |

Resolved: package ID (`XConnect` is taken by another author; `XConnect.Sdk` is free, re-checked today with `tools/check_package_id.py`), repository and project URLs, deprecated `licenseUrl`, author/copyright, relative README links, `.snupkg` not in `.gitignore` (fixed), CRLF risk on Windows checkouts (`.gitattributes` added).

## Status by area

| Area | Status |
| --- | --- |
| **Security** | See below. No known open issue above "non-blocking". |
| **Package metadata** | Done. ID `XConnect.Sdk`, MIT as an SPDX expression (NuGet adds the standard `licenses.nuget.org/MIT` URL itself; the deprecated `aka.ms` URL is gone), author and copyright agree (Chaitanya Krishna Jonnagaddala, as in LICENSE.txt), description with an "unofficial, not affiliated with X Corp" notice, tags, README, `projectUrl`, `repository` (type git, URL, branch, commit). |
| **Repository URL** | Taken from the git remote `origin`: `https://github.com/mechaitanya/TwitterConnect`. Confirmed: the repository exists, is public, MIT, default branch `main`; `mechaitanya/XConnect` returns 404. The repository name no longer matches the product. If it is renamed, change `PackageProjectUrl`, `RepositoryUrl` (`XConnect.csproj`) and the README links together. |
| **Package icon** | None, deliberately. The only logo in history (`logo-min.png`) is the old TwitterConnect artwork; using X/Twitter branding on an unofficial SDK is inappropriate. |
| **Package contents** | One `.nupkg` and one `.snupkg`. Package: DLL, XML docs (1,499 documented members), README, LICENSE.txt, nuspec. No stray files. One dependency, `Microsoft.Extensions.Http` 11.0.0. |
| **Symbols / Source Link** | Portable PDB in the `.snupkg` with a Source Link map to `raw.githubusercontent.com/mechaitanya/TwitterConnect/<commit>/*`, deterministic build, untracked sources embedded. Correct once B1 is done. |
| **Package verification script** | `tools/verify_package.py` checks metadata, license expression, deprecated URL, author/copyright, dependencies, stray files, README links and credentials, XML docs, the symbol package and the Source Link commit. `tools/test_verify_package.py` (16 cases) proves it passes the good package and fails on 15 deliberate breakages; it also runs in CI. `tools/check_package_id.py` checks nuget.org just before publishing. |
| **Package validation** | `EnablePackageValidation` is on with no baseline, because nothing was ever published under this ID, so there is no API baseline to compare against. 2.0 API compatibility is covered by tests (`CompatibilityAndDiTests`). |
| **Documentation** | README code compiles (all three snippets, built against the library, warnings as errors). Every relative link in README and docs resolves; external links return 200. Webhooks, backlog, verification notes, migration and integration guides are current. |
| **Build** | Debug and Release: 0 warnings, 0 errors, warnings as errors, `latest-recommended` analyzers (macOS). |
| **Tests** | 346 unit tests pass; the integration project has 13 gate tests passing and 14 live tests skipped. Mutation checks: removing the shared refresh lock, the connect timeout, a model field or a valid query parameter makes tests fail. |
| **AOT / trimming** | Native AOT publish and run pass on macOS x64 with no IL or trim warnings (the only warnings are Apple linker messages about .NET's own native libraries). A trimmed non-AOT publish also runs. Linux and Windows: not run. |
| **Cross-platform** | **macOS x64:** the 11 `run:` commands of `ci.yml`, extracted from the YAML and executed in a clean copy, all pass. **Linux x64:** the same commands pass in the official `mcr.microsoft.com/dotnet/sdk:11.0` container (Debug and Release builds 0 warnings, 346 unit tests, gated integration tests, samples, Native AOT publish and run, trimmed publish and run, pack, package verification). **Windows:** not run. **GitHub Actions itself:** not run (the YAML parses and its commands work, but runner images, `setup-dotnet` for 11.0.x and the Windows AOT toolchain are untested). Linux arm64 was not tested. |
| **Live integration** | Not run (B4). Readiness: read-only tests run independently (`Category=Live`); writes need the exact phrase `CREATE-AND-DELETE-A-REAL-POST`; deletion is limited to Posts the test created (ledger, unit-tested); media tests need your own files; token-refresh tests need a separate `XCONNECT_LIVE_ALLOW_TOKEN_REFRESH=1`; secrets are never printed; the cleanup of every test is tabulated. |
| **Coverage** | 132 supported, 82 not implemented, 47 not supported, 6 access-restricted, 1 not applicable, of 268. Not inflated; every supported operation has a test and a call count. |

## Security fixes in this phase

- **Token refresh:** one lock per token-store instance now serialises refresh across every authenticator that shares the store (test: 6 independently built clients, 18 concurrent calls, exactly one refresh); the rotated token is saved before use and with `CancellationToken.None`, so a cancelling caller cannot lose it; the old per-instance lock is gone.
- **Streams:** `ConnectTimeout` (30 s) stops a silent server from hanging a stream forever; caller cancellation is never mistaken for it; non-positive timeouts are rejected instead of looping; reconnect backoff is jittered; a keep-alive-only server backs off instead of reconnecting in a tight loop; abandoning the stream releases the response.
- **Retries:** jittered exponential backoff (bounded by `MaxDelay`, `Jitter = 0` gives exact delays); a 429 whose reset time is in the past waits at least one second; Post creation is never retried after a 5xx or a lost connection (tested for 503, 502 and a network error).
- **Media:** simple uploads are bounded while reading (an endless stream is rejected after about 5 MB, a seekable over-limit stream without reading it); chunked uploads read exactly the declared length per segment and refuse to finalize a stream that grew; empty streams are rejected; cancellation between segments is covered.
- From the previous phase: no secret in `ToString`, dot-segment path IDs rejected, overflow-safe rate-limit parsing, RFC 5849 query decoding.

## Non-blocking

| Issue | Note |
| --- | --- |
| Refresh coordination stops at the process boundary | Two processes sharing one refresh token can still race if X rotates tokens. Use one token per process or an externally locking store. |
| No automatic recovery from a 401 | Refresh happens from the local expiry only. |
| Pre-signed uploads and downloads have no timeout of their own | Pass a `CancellationToken` with a timeout. |
| Non-stream response bodies are read whole; streamed lines have no maximum length | Matches X's small JSON responses and line-delimited streams. |
| Assumptions awaiting live proof | `tweet.fields` accepted on streams; media segment limit; APPEND not retried; POST retried after 429; chunked pre-signed upload. Listed in [verification-notes.md](verification-notes.md). |
| Package is unsigned | NuGet author signing needs a certificate. |
| Repository name differs from the product | Cosmetic, but visible on the package page. |
| `docs/` links in the README point at `main` | They follow the default branch, not the release tag; pin to a tag after tagging if you want them frozen. |

## Deferred

All unimplemented API areas, with reasons, in [backlog.md](backlog.md): Activity API (next), Community Notes, DM media download, developer account, articles, compliance token routes, Media library and Amplify (docs unconfirmed), Livestream API (separate product), the Enterprise streams, and X Chat (deliberately unsupported).

## Recommended version

Publish **`2.1.0-preview.1`** (a pre-release, which NuGet hides from default searches) as soon as B1 and B2 are done, then **`2.1.0`** once B3 (CI green on all three systems) and B4 (live suite run) are complete. A preview is the honest label for a package whose X-dependent behaviour has never met the real API.
`2.1.0` is the right stable number for this ID: it is the first release under `XConnect.Sdk`, the 2.0 types (`XClient`, `XCredentials`, ...) are preserved, and the API is additive. To publish a preview, pass `-p:Version=2.1.0-preview.1` to `dotnet pack` (the project file keeps `2.1.0`).

## Preview release checklist (each step is yours; nothing here has been done)

Nothing below was executed on your behalf: no commit, no branch, no push, no publish.

1. **Approve** the commit scope (see "What would be committed"), including the deletion of the TwitterConnect 1.x sources and logo.
2. **Branch, so `main` is not modified:** `git switch -c release/2.1.0-preview.1` (carries your staged and unstaged work over unchanged).
3. **Commit:** `git add -A && git commit` (`.gitignore` excludes build output; the 76 untracked files were checked for secrets and junk).
4. **Push the branch** (`git push -u origin release/2.1.0-preview.1`) and let CI run on all three systems; fix what Windows reveals.
5. **Live run:** follow [the order in integration-testing.md](integration-testing.md#recommended-order-for-the-preview-release-run) with a test account; record the results in [verification-notes.md](verification-notes.md).
6. **Merge to `main`** when satisfied (this is the step that changes `main`), so the README links resolve.
7. **Check the ID again:** `python3 tools/check_package_id.py XConnect.Sdk 2.1.0-preview.1` (must print OK).
8. **Pack from the clean, pushed commit:**
   `dotnet pack src/XConnect -c Release -p:Version=2.1.0-preview.1 -o artifacts`
9. **Verify with no flags (must pass):** `python3 tools/verify_package.py artifacts && python3 tools/test_verify_package.py artifacts`
10. **Publish** the two files yourself with your API key:
    `dotnet nuget push artifacts/XConnect.Sdk.2.1.0-preview.1.nupkg --api-key <your key> --source https://api.nuget.org/v3/index.json`
    (the `.snupkg` is pushed automatically alongside it when it sits in the same folder).
11. After it appears on nuget.org, install it into a scratch project and run one call.
