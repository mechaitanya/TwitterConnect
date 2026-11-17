# Publishing Posts

All Post writes go through `client.Posts` and a single request type, `CreatePostRequest`. Set only the properties that apply.
Posting requires user-context authentication (OAuth 1.0a, or OAuth 2.0 with the `tweet.write` scope).

```csharp
// Plain Post
await client.Posts.CreateAsync(new CreatePostRequest { Text = "Hello!" });

// Reply, quote
await client.Posts.ReplyAsync("1445880548472328192", "Thanks!");
await client.Posts.QuoteAsync("1445880548472328192", "Worth reading");   // X documents quote Posting as Enterprise-only

// Poll: 2-4 options of 1-25 characters, 5-10080 minutes. A poll cannot be combined with media.
await client.Posts.CreateAsync(new CreatePostRequest
{
    Text = "Tabs or spaces?",
    Poll = new XPostPoll { Options = ["Tabs", "Spaces"], DurationMinutes = 1440 },
    ReplySettings = XReplySettings.Following,
});

// Media (see media.md), with tagged users
await client.Posts.CreateAsync(new CreatePostRequest
{
    Text = "Our announcement",
    Media = new XPostMedia { MediaIds = [media.MediaId], TaggedUserIds = ["2244994945"] },
});

// Edit a Post that is still eligible for editing
await client.Posts.EditAsync(previousPostId, new CreatePostRequest { Text = "Fixed typo" });

// Delete, hide and unhide replies
await client.Posts.DeleteAsync(postId);
await client.Posts.HideReplyAsync(replyId);
await client.Posts.UnhideReplyAsync(replyId);
```

Media Posts also accept `Title`, `Description`, `Embeddable`, `PreviewMediaId` and one `CallToActions` variant (`AppInstall`, `VisitSite` or `WatchNow`; URLs must be HTTPS). `GetRepostsAsync` and `GetRepostsOfMeAsync` read reposts as Post objects.

Other supported fields: `Geo` (place ID), `CommunityId`, `ForSuperFollowersOnly`, `Nullcast`, `PaidPartnership`,
`MadeWithAi`, `ShareWithFollowers`, `DirectMessageDeepLink`, `CardUri`, `Clip`. Text is limited to 280 characters unless the
account has X Premium. The SDK validates what the API documents (text or media present, as X requires; poll shape; at most 20 media IDs
and 10 tagged users) and throws `ArgumentException` **before** sending, so a malformed request never costs rate limit.

## Threads

X has no thread endpoint: a thread is a chain of replies. `CreateThreadAsync` creates the Posts one at a time, each replying to
the previous one:

```csharp
var thread = await client.Posts.CreateThreadAsync(["First post", "Second post", "Third post"]);

if (!thread.IsComplete)
{
    // thread.Posts holds what was created; thread.FailedIndex and thread.Error say where and why it stopped.
}
```

Creation stops at the first failure. Posts that were already created are **not** deleted, and the failed Post is **not**
retried, because a failed `POST` may still have succeeded. Resume by passing the remaining texts and replying to the last
created Post (`new CreatePostRequest { Text = ..., Reply = new XPostReply { InReplyToTweetId = lastId } }`).

## Reading

```csharp
var post = await client.Posts.GetAsync(id, new XFieldSelection
{
    PostFields = [XPostFields.CreatedAt, XPostFields.PublicMetrics],
    Expansions = [XPostExpansions.AuthorId],
    UserFields = [XUserFields.Username],
});
// post.Data.PublicMetrics.LikeCount, post.Includes.Users ...
```

Also: `GetManyAsync` (up to 100), `GetUserPostsAsync`, `GetMentionsAsync`, `GetHomeTimelineAsync`, `GetLikedPostsAsync`,
`GetRepostingUsersAsync`, `GetLikingUsersAsync`, `GetQuotePostsAsync`, `SearchRecentAsync`, `SearchAllAsync` (full archive,
needs an access level that includes it), `GetCountsRecentAsync`, `GetCountsAllAsync` and `GetAnalyticsAsync`.

- **Replies:** X has no replies endpoint. `GetRepliesAsync(conversationId)` runs a recent search for
  `conversation_id:{id} is:reply`, so it covers the last seven days only.
- **Metrics:** public engagement counts come from `XPostFields.PublicMetrics` (and `XUserFields.PublicMetrics` for accounts).
  Private metrics need `NonPublicMetrics`/`OrganicMetrics` fields and the owner's user-context token. Per-Post analytics over a
  time window use `GetAnalyticsAsync`.
- Large Post IDs are strings throughout, so no precision is lost.
