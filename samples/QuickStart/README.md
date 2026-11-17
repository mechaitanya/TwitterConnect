# QuickStart sample

A small console app that looks up an X user with the XConnect SDK. It is a sample, not part of the package.

```
dotnet run --project samples/QuickStart -- <username>      # lookup by username (default: XDevelopers)
dotnet run --project samples/QuickStart -- --id <user-id>  # lookup by user ID
dotnet run --project samples/QuickStart -- --me            # authenticated user; simulator only (see below)
```

## Real X API (default)

Put your app-only bearer token in `appsettings.Local.json` (git-ignored) under `XConnect:BearerToken`, or set `XCONNECT_BEARER_TOKEN`. `appsettings.json` holds empty placeholders and is tracked: never put a real value there. The sample talks to `https://api.x.com/`. An account without credits gets `PaymentRequired (HTTP 402)`.

## Running against the local X API Playground (optional, simulated)

> **Warning: the Playground is a simulator.** Its responses are simulated. They do **not** verify real X API compatibility, OAuth behaviour, permissions or billing. Never describe a Playground result as real X API verification.

1. Start the Playground (a separate project, `https://github.com/xdevplatform/playground`; its licence is not yet confirmed upstream, so this repo does not include or build it):
   `cd ~/Desktop/playground && HOME=$PWD/.home ./playground start --port 18080 --host 127.0.0.1`
2. Point the sample at it and confirm it is a simulator. The address is not hardcoded anywhere in the sample:
   `export XCONNECT_BASE_ADDRESS=http://127.0.0.1:18080 XCONNECT_SIMULATOR=1`
3. Run:
   `dotnet run --project samples/QuickStart -- --id 0` looks up the seeded Playground User (`GET /2/users/0`).
   `dotnet run --project samples/QuickStart -- --me` calls `GET /2/users/me`.

### Exact activation rules

Simulator mode is **never inferred from the host name**; it needs both variables.

| `XCONNECT_BASE_ADDRESS` | `XCONNECT_SIMULATOR` | Result |
| --- | --- | --- |
| unset | unset, `0` or `false` | Real X API, real bearer token |
| `https://api.x.com` | unset, `0` or `false` | Real X API (same as unset); `http://api.x.com` is refused |
| any other host | unset, `0` or `false` | **Refused** with a message: the sample will not send your real token to another host |
| any other host (absolute http or https URL) | `1` or `true` | Simulator mode |
| unset or `api.x.com` | `1` or `true` | Refused |

Any other `XCONNECT_SIMULATOR` value is refused.

In simulator mode the sample:
- sends the placeholder token `test_token` (override with `XCONNECT_PLAYGROUND_TOKEN`) and **never** your real bearer token (neither from the environment nor from the settings file);
- for `--me` only, adds the nonstandard `X-Auth-Method: oauth2` header the Playground needs to treat a request as user context. The real API never gets that header or the placeholder;
- prints a `SIMULATOR` line so a run cannot be mistaken for a real one.

Note: `XCONNECT_PLAYGROUND_TOKEN` is sent to whatever host you configure, so use a throwaway value, not a real credential.

Known Playground gap: `GET /2/users/by/username/playground_user` returns "Endpoint not found" (cause unconfirmed), so use `--id 0` for the seeded user.

## Back to the real API

`unset XCONNECT_BASE_ADDRESS XCONNECT_SIMULATOR`. Behaviour is then identical to before.

## Tests

`dotnet test tests/QuickStart.Tests` (stubbed, no network). They check the sample's own logic only.
