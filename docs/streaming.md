# Streaming

Streaming endpoints need an **app-only Bearer token** and an access level that includes them.

```csharp
await client.Streaming.AddRulesAsync([new XStreamRuleDraft("dotnet has:media", "dotnet-media")]);

await foreach (var e in client.Streaming.StreamFilteredAsync(
    new XStreamOptions { Fields = new XFieldSelection { PostFields = [XPostFields.CreatedAt] } },
    cancellationToken))
{
    Console.WriteLine($"{e.MatchingRules?[0].Tag}: {e.Post?.Text}");
}
```

- `StreamSampleAsync` (`/2/tweets/sample/stream`) and `StreamFilteredAsync` (`/2/tweets/search/stream`) return an `IAsyncEnumerable<XStreamEvent>`.
- Rules: `GetRulesAsync`, `AddRulesAsync(rules, dryRun)`, `DeleteRulesAsync(ids)`.
- **Not buffered:** the response is read line by line as it arrives; events are yielded immediately.
- **Keep-alive:** X sends a blank line about every 20 seconds. Blank lines are skipped; if nothing arrives for `KeepAliveTimeout` (30 s by default) the connection is treated as dead and re-established.
- **Connection timeout:** `ConnectTimeout` (30 s by default) bounds the wait for X to answer a connection attempt. A server that accepts the connection and then goes silent is treated as a failed attempt and retried with backoff; cancelling your own token is never reported as a timeout. `KeepAliveTimeout`, `ConnectTimeout` and `ReconnectDelay` must be positive, otherwise the stream throws `ArgumentOutOfRangeException` instead of looping. Both timers use the system clock.
- **Reconnection:** dropped connections, network errors, 5xx and 429 reconnect with exponential backoff (`ReconnectDelay` 1 s doubling to `MaxReconnectDelay` 5 min; at least `RateLimitDelay` 1 min after a 429). Each delay is randomised downwards by up to `Jitter` (default 20%) so many consumers that lose a stream together do not reconnect in lockstep; the post-429 wait is lengthened by up to the same fraction, never shortened. The counter resets only when a real event arrives: a server that sends nothing but keep-alive lines and then drops still backs off. After `MaxReconnects` (10) consecutive failures the last failure is thrown. Other 4xx responses (bad credentials, no access, malformed query) are thrown at once because reconnecting will not fix them.
- A malformed line is skipped rather than ending a long-lived stream. Cancel with the token you pass in.
- `BackfillMinutes` (0-5) asks X to replay missed Posts after a reconnect where your plan allows it.

**Partitioned (Enterprise) streams:** `StreamPartitionAsync(XPostStreamKind.Sample10 | Firehose | FirehoseEnglish | FirehoseJapanese | FirehoseKorean | FirehosePortuguese, partition)` streams those endpoints with the required
`partition` parameter. They need Enterprise access (X answers 403, thrown at once as `XApiException` with `Authorization`) and have not been exercised live. `GetRuleCountsAsync` reports rule usage. A second connection to the filtered stream
(documented limit: one) is rejected with 409 and surfaces as `XApiErrorKind.Conflict`, thrown rather than retried.

Compliance, label and likes streams are not implemented; see [api-coverage.md](api-coverage.md).
