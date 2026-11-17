# Webhooks and Account Activity

`client.Webhooks` wraps X's **management** endpoints: registering webhooks, re-validating them, replaying missed events, linking a webhook to the filtered stream and
managing Account Activity subscriptions. **Receiving events is your job**: X calls a public HTTPS URL that you host. `XWebhookSecurity` handles the cryptography.

Plan access for these endpoints is decided by X; `403` surfaces as `XApiErrorKind.Authorization`. None of this has been exercised against the live API.

## Requirements X documents

- The URL must be HTTPS, publicly reachable, and **must not contain a port**. `CreateAsync` rejects anything else before sending.
- X sends a CRC check (GET with `crc_token`) on registration, hourly and on manual re-validation, and expects an answer within 10 seconds.
- A webhook becomes invalid if the CRC answer is wrong, the endpoint redirects, TLS fails or it is persistently unreachable.

## Answering the CRC check and verifying events

Both use `sha256=` + Base64(HMAC-SHA256). The key is your app's **OAuth 2.0 client secret** (recommended, event header `X-Twitter-Webhooks-Signature-OAuth2`) or its
**OAuth 1.0 consumer secret** (legacy, header `X-Twitter-Webhooks-Signature`). The bearer token is not the key.

```csharp
// ASP.NET Core minimal API sketch
app.MapGet("/webhooks/x", (string crc_token) =>
    Results.Json(new { response_token = XWebhookSecurity.CreateCrcResponseToken(crc_token, clientSecret) }));

app.MapPost("/webhooks/x", async (HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);               // verify the exact bytes, before parsing
    var header = request.Headers["X-Twitter-Webhooks-Signature-OAuth2"].ToString();

    return XWebhookSecurity.VerifySignature(buffer.ToArray(), header, clientSecret)
        ? Results.Ok()
        : Results.Unauthorized();
});
```

Keep the secret in a secret store. Reject (and do not process) any event whose signature does not verify, and return quickly: do the real work after responding.

## Managing webhooks

```csharp
var created = await client.Webhooks.CreateAsync("https://example.com/webhooks/x");
if (created.IsSuccess)
{
    var id = created.Data!.Id!;
    await client.Webhooks.ValidateAsync(id);                                   // trigger a CRC re-check
    var replay = await client.Webhooks.CreateReplayJobAsync(                   // up to the last 24 h
        id, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1));
}

// Account Activity: subscribe the authenticated user (user context), then manage as the app
await client.Webhooks.CreateSubscriptionAsync(webhookId);
var subscriptions = await client.Webhooks.GetSubscriptionsAsync(webhookId);    // app-only
```

Authentication per call is chosen for you: the webhook CRUD accepts any method; replay, stream links, subscription lists, removal and counts are **app-only**; creating
and validating a subscription need **user context**. Register both an app-only and a user method when you use both.

## Not covered

The older Activity API (`/2/activity/*`) is a separate, still-unimplemented module: see [backlog](backlog.md).
