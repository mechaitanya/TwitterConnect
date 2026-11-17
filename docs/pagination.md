# Pagination

List endpoints return a page plus a token for the next one in `XResponse<T>.NextToken` (from `meta.next_token`). The request
side takes the token in a `PaginationToken` property. XConnect sends it as `pagination_token`, except `Users.SearchAsync`, which the
API paginates with `next_token`; you never need to know the difference.

## Manual paging

```csharp
var page = await client.Users.GetFollowersAsync(userId, new XPageQuery { MaxResults = 1000 });
while (true)
{
    foreach (var user in page.Data ?? []) { /* ... */ }
    if (page.NextToken is not { } token) break;
    page = await client.Users.GetFollowersAsync(userId, new XPageQuery { MaxResults = 1000, PaginationToken = token });
}
```

## Automatic traversal

`XPaginator` works with any list method:

```csharp
await foreach (var post in XPaginator.ItemsAsync(
    (token, ct) => client.Posts.SearchRecentAsync("from:xdevelopers", new XSearchQuery { PaginationToken = token, MaxResults = 100 }, ct),
    new XPaginationOptions { MaxPages = 5, MaxItems = 300 },
    cancellationToken))
{
    Console.WriteLine(post.Text);
}
```

- `PagesAsync` yields each `XResponse` (with its `Meta`, `Includes` and `RateLimit`); `ItemsAsync` yields the items and throws
  `XApiException` if a page fails.
- Traversal is always bounded: `MaxPages` defaults to **10**, and `MaxItems` is optional.
- If X returns a token you have already used, traversal stops instead of looping forever.
- Cancellation is honoured between and during pages.
- Each page is a separate request that counts against the rate limit; large traversals can hit it. See [rate-limits.md](rate-limits.md).

Not every endpoint paginates the same way: some return everything in one response (for example `GetManyAsync`), and page sizes differ
(for example 5-100 for user Posts, up to 1000 for followers). The valid range is documented on each method; X reports a violation as `400`.
