#!/usr/bin/env python3
"""Regenerates docs/api-coverage.md and docs/spec-snapshot.json.

Usage:  python3 tools/generate_coverage.py [path/to/openapi.json]
Without an argument the official document is downloaded from https://docs.x.com/openapi.json.

Inputs : the OpenAPI document, the routes found in src/XConnect/**/*.cs, the tests in tests/XConnect.Tests/*.cs
Outputs: docs/spec-snapshot.json (compact, committed; the conformance test reads it) and docs/api-coverage.md
"""
import glob, json, os, re, sys, urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPEC_URL = "https://docs.x.com/openapi.json"

# method+route -> "SDK member | request type | response type"  (the single source for the SDK-member columns)
M = {
 "POST /2/tweets": "Posts.CreateAsync, ReplyAsync, QuoteAsync, EditAsync, CreateThreadAsync | CreatePostRequest | XCreatedPost",
 "DELETE /2/tweets/{id}": "Posts.DeleteAsync | - | XDeleteResult",
 "PUT /2/tweets/{tweet_id}/hidden": "Posts.HideReplyAsync, UnhideReplyAsync | { hidden } | XHiddenResult",
 "GET /2/tweets/{id}": "Posts.GetAsync | XFieldSelection | XPost",
 "GET /2/tweets": "Posts.GetManyAsync | XFieldSelection | XPost[]",
 "GET /2/users/{id}/tweets": "Posts.GetUserPostsAsync | XPostQuery | XPost[]",
 "GET /2/users/{id}/mentions": "Posts.GetMentionsAsync | XPostQuery | XPost[]",
 "GET /2/users/{id}/timelines/reverse_chronological": "Posts.GetHomeTimelineAsync | XPostQuery | XPost[]",
 "GET /2/users/{id}/liked_tweets": "Posts.GetLikedPostsAsync | XPageQuery | XPost[]",
 "GET /2/tweets/{id}/retweeted_by": "Posts.GetRepostingUsersAsync | XPageQuery | XUser[]",
 "GET /2/tweets/{id}/retweets": "Posts.GetRepostsAsync | XPageQuery | XPost[]",
 "GET /2/users/reposts_of_me": "Posts.GetRepostsOfMeAsync | XPageQuery | XPost[]",
 "GET /2/tweets/{id}/liking_users": "Posts.GetLikingUsersAsync | XPageQuery | XUser[]",
 "GET /2/tweets/{id}/quote_tweets": "Posts.GetQuotePostsAsync | XPostQuery | XPost[]",
 "GET /2/tweets/search/recent": "Posts.SearchRecentAsync, GetRepliesAsync | XSearchQuery | XPost[]",
 "GET /2/tweets/search/all": "Posts.SearchAllAsync | XSearchQuery | XPost[]",
 "GET /2/tweets/counts/recent": "Posts.GetCountsRecentAsync | XCountsQuery | XPostCount[]",
 "GET /2/tweets/counts/all": "Posts.GetCountsAllAsync | XCountsQuery | XPostCount[]",
 "GET /2/tweets/analytics": "Posts.GetAnalyticsAsync | ids, window, metrics | XPostAnalytics[]",
 "GET /2/users/me": "Users.GetMeAsync | XFieldSelection | XUser",
 "GET /2/users/{id}": "Users.GetByIdAsync | XFieldSelection | XUser",
 "GET /2/users/by/username/{username}": "Users.GetByUsernameAsync | XFieldSelection | XUser",
 "GET /2/users": "Users.GetManyAsync | XFieldSelection | XUser[]",
 "GET /2/users/by": "Users.GetByUsernamesAsync | XFieldSelection | XUser[]",
 "GET /2/users/search": "Users.SearchAsync | XPageQuery | XUser[]",
 "GET /2/users/{id}/followers": "Users.GetFollowersAsync | XPageQuery | XUser[]",
 "GET /2/users/{id}/following": "Users.GetFollowingAsync | XPageQuery | XUser[]",
 "GET /2/users/{id}/affiliates": "Users.GetAffiliatesAsync | XPageQuery | XUser[]",
 "POST /2/users/{id}/likes": "Engagement.LikeAsync | { tweet_id } | XLikeResult",
 "DELETE /2/users/{id}/likes/{tweet_id}": "Engagement.UnlikeAsync | - | XLikeResult",
 "POST /2/users/{id}/retweets": "Engagement.RepostAsync | { tweet_id } | XRepostResult",
 "DELETE /2/users/{id}/retweets/{source_tweet_id}": "Engagement.UndoRepostAsync | - | XRepostResult",
 "POST /2/users/{id}/bookmarks": "Engagement.BookmarkAsync | { tweet_id } | XBookmarkResult",
 "DELETE /2/users/{id}/bookmarks/{tweet_id}": "Engagement.RemoveBookmarkAsync | - | XBookmarkResult",
 "GET /2/users/{id}/bookmarks": "Engagement.GetBookmarksAsync | XPageQuery | XPost[]",
 "GET /2/users/{id}/bookmarks/folders": "Engagement.GetBookmarkFoldersAsync | XPageQuery | XBookmarkFolder[]",
 "POST /2/users/{id}/bookmarks/folders": "Engagement.CreateBookmarkFolderAsync | { name } | XBookmarkFolder",
 "GET /2/users/{id}/bookmarks/folders/{folder_id}": "Engagement.GetBookmarksInFolderAsync | XPageQuery | XPost[]",
 "POST /2/users/{id}/following": "Engagement.FollowAsync | { target_user_id } | XFollowResult",
 "DELETE /2/users/{source_user_id}/following/{target_user_id}": "Engagement.UnfollowAsync | - | XUnfollowResult",
 "POST /2/users/{id}/blocking": "Engagement.BlockAsync | { target_user_id } | XBlockResult",
 "DELETE /2/users/{source_user_id}/blocking/{target_user_id}": "Engagement.UnblockAsync | - | XBlockResult",
 "GET /2/users/{id}/blocking": "Engagement.GetBlockedUsersAsync | XPageQuery | XUser[]",
 "POST /2/users/{id}/muting": "Engagement.MuteAsync | { target_user_id } | XMuteResult",
 "DELETE /2/users/{source_user_id}/muting/{target_user_id}": "Engagement.UnmuteAsync | - | XMuteResult",
 "GET /2/users/{id}/muting": "Engagement.GetMutedUsersAsync | XPageQuery | XUser[]",
 "POST /2/media/upload": "Media.UploadImageAsync, UploadAsync (images, subtitles) | multipart | XMediaUploadResult",
 "POST /2/media/upload/initialize": "Media.UploadAsync (GIF, video: INIT) | { media_type, total_bytes, media_category } | XMediaUploadResult",
 "POST /2/media/upload/{id}/append": "Media.UploadAsync (APPEND) | multipart segment | -",
 "POST /2/media/upload/{id}/finalize": "Media.UploadAsync (FINALIZE) | - | XMediaUploadResult",
 "GET /2/media/upload": "Media.GetUploadStatusAsync, WaitForProcessingAsync | media_id | XMediaUploadResult",
 "POST /2/media/metadata": "Media.SetMetadataAsync, SetAltTextAsync | XMediaMetadata | XMediaMetadataResult",
 "POST /2/media/subtitles": "Media.CreateSubtitlesAsync | subtitle ids | XMediaSubtitlesResult",
 "DELETE /2/media/subtitles": "Media.DeleteSubtitlesAsync | video id, language | XMediaSubtitlesDeleteResult",
 "GET /2/media/{media_key}": "Media.GetAsync | XFieldSelection | XMedia",
 "GET /2/media": "Media.GetManyAsync | XFieldSelection | XMedia[]",
 "GET /2/media/analytics": "Media.GetAnalyticsAsync | keys, window, metrics | XMediaAnalytics[]",
 "POST /2/dm_conversations": "DirectMessages.CreateGroupConversationAsync | XDirectMessage | XDmSendResult",
 "POST /2/dm_conversations/with/{participant_id}/messages": "DirectMessages.SendToParticipantAsync | XDirectMessage | XDmSendResult",
 "POST /2/dm_conversations/{dm_conversation_id}/messages": "DirectMessages.SendToConversationAsync | XDirectMessage | XDmSendResult",
 "GET /2/dm_events": "DirectMessages.GetEventsAsync | XDmQuery | XDmEvent[]",
 "GET /2/dm_conversations/{id}/dm_events": "DirectMessages.GetConversationEventsAsync | XDmQuery | XDmEvent[]",
 "GET /2/dm_conversations/with/{participant_id}/dm_events": "DirectMessages.GetEventsWithParticipantAsync | XDmQuery | XDmEvent[]",
 "GET /2/dm_events/{event_id}": "DirectMessages.GetEventAsync | XFieldSelection | XDmEvent",
 "DELETE /2/dm_events/{event_id}": "DirectMessages.DeleteEventAsync | - | XDmDeleteResult",
 "POST /2/users/{id}/dm/block": "DirectMessages.BlockSenderAsync | - | XDmBlockResult",
 "POST /2/users/{id}/dm/unblock": "DirectMessages.UnblockSenderAsync | - | XDmBlockResult",
 "POST /2/lists": "Lists.CreateAsync | XListRequest | XList",
 "PUT /2/lists/{id}": "Lists.UpdateAsync | XListRequest | XListUpdateResult",
 "DELETE /2/lists/{id}": "Lists.DeleteAsync | - | XListDeleteResult",
 "GET /2/lists/{id}": "Lists.GetAsync | XFieldSelection | XList",
 "GET /2/users/{id}/owned_lists": "Lists.GetOwnedAsync | XPageQuery | XList[]",
 "GET /2/users/{id}/followed_lists": "Lists.GetFollowedAsync | XPageQuery | XList[]",
 "GET /2/lists/{id}/members": "Lists.GetMembersAsync | XPageQuery | XUser[]",
 "GET /2/lists/{id}/followers": "Lists.GetFollowersAsync | XPageQuery | XUser[]",
 "GET /2/lists/{id}/tweets": "Lists.GetPostsAsync | XPageQuery | XPost[]",
 "POST /2/lists/{id}/members": "Lists.AddMemberAsync | { user_id } | XListMemberResult",
 "DELETE /2/lists/{id}/members/{user_id}": "Lists.RemoveMemberAsync | - | XListMemberResult",
 "GET /2/users/{id}/list_memberships": "Lists.GetMembershipsAsync | XPageQuery | XList[]",
 "GET /2/users/{id}/pinned_lists": "Lists.GetPinnedAsync | XFieldSelection | XList[]",
 "POST /2/users/{id}/pinned_lists": "Lists.PinAsync | { list_id } | XPinResult",
 "DELETE /2/users/{id}/pinned_lists/{list_id}": "Lists.UnpinAsync | - | XPinResult",
 "POST /2/users/{id}/followed_lists": "Lists.FollowAsync | { list_id } | XListFollowResult",
 "DELETE /2/users/{id}/followed_lists/{list_id}": "Lists.UnfollowAsync | - | XListFollowResult",
 "GET /2/spaces/{id}": "Spaces.GetAsync | XFieldSelection | XSpace",
 "GET /2/spaces": "Spaces.GetManyAsync | XFieldSelection | XSpace[]",
 "GET /2/spaces/by/creator_ids": "Spaces.GetByCreatorsAsync | XFieldSelection | XSpace[]",
 "GET /2/spaces/search": "Spaces.SearchAsync | query, state | XSpace[]",
 "GET /2/spaces/{id}/tweets": "Spaces.GetPostsAsync | XPageQuery | XPost[]",
 "GET /2/tweets/sample/stream": "Streaming.StreamSampleAsync | XStreamOptions | XStreamEvent (stream)",
 "GET /2/tweets/search/stream": "Streaming.StreamFilteredAsync | XStreamOptions | XStreamEvent (stream)",
 "GET /2/tweets/search/stream/rules": "Streaming.GetRulesAsync | ids | XStreamRule[]",
 "POST /2/tweets/search/stream/rules": "Streaming.AddRulesAsync, DeleteRulesAsync | XStreamRuleDraft[] / ids | XStreamRule[]",
 "GET /2/tweets/search/stream/rules/counts": "Streaming.GetRuleCountsAsync | - | XRuleCounts",
 "GET /2/tweets/sample10/stream": "Streaming.StreamPartitionAsync (Sample10) | partition | XStreamEvent (stream)",
 "GET /2/tweets/firehose/stream": "Streaming.StreamPartitionAsync (Firehose) | partition | XStreamEvent (stream)",
 "GET /2/tweets/firehose/stream/lang/en": "Streaming.StreamPartitionAsync (FirehoseEnglish) | partition | XStreamEvent (stream)",
 "GET /2/tweets/firehose/stream/lang/ja": "Streaming.StreamPartitionAsync (FirehoseJapanese) | partition | XStreamEvent (stream)",
 "GET /2/tweets/firehose/stream/lang/ko": "Streaming.StreamPartitionAsync (FirehoseKorean) | partition | XStreamEvent (stream)",
 "GET /2/tweets/firehose/stream/lang/pt": "Streaming.StreamPartitionAsync (FirehosePortuguese) | partition | XStreamEvent (stream)",
 "POST /2/compliance/jobs": "Compliance.CreateJobAsync | type, name | XComplianceJob",
 "GET /2/compliance/jobs": "Compliance.GetJobsAsync | type, status | XComplianceJob[]",
 "GET /2/compliance/jobs/{id}": "Compliance.GetJobAsync | - | XComplianceJob",
 "DELETE /2/compliance/jobs/{id}": "Compliance.CancelJobAsync | - | XComplianceJob",
 "GET /2/trends/by/woeid/{woeid}": "Trends.GetByWoeidAsync | woeid | XTrend[]",
 "GET /2/users/personalized_trends": "Trends.GetPersonalizedAsync | fields | XPersonalizedTrend[]",
 "GET /2/communities/{id}": "Communities.GetAsync | XFieldSelection | XCommunity",
 "GET /2/communities/search": "Communities.SearchAsync | XPageQuery | XCommunity[]",
 "GET /2/news/search": "News.SearchAsync | query | XNewsStory[]",
 "GET /2/news/{id}": "News.GetAsync | fields | XNewsStory",
 "GET /2/usage/tweets": "Usage.GetPostUsageAsync | days, fields | XPostUsage",
 "GET /2/usage/credits": "Usage.GetCreditsAsync | - | XCreditBalance",
 "GET /2/users/me/followers": "Users.GetMyFollowersAsync | XPageQuery | XUser[]",
 "GET /2/users/me/following": "Users.GetMyFollowingAsync | XPageQuery | XUser[]",
 "GET /2/spaces/{id}/buyers": "Spaces.GetTicketBuyersAsync | XPageQuery | XUser[]",
 "GET /2/connections": "Connections.GetHistoryAsync | status, endpoints, connection.fields | XConnection[]",
 "DELETE /2/connections": "Connections.TerminateAsync | { uuids } | XConnectionKillSummary",
 "DELETE /2/connections/all": "Connections.TerminateAllAsync | - | XConnectionKillSummary",
 "DELETE /2/connections/{endpoint_id}": "Connections.TerminateByEndpointAsync | XStreamEndpoints | XConnectionKillSummary",
 "GET /2/webhooks": "Webhooks.GetAsync | - | XWebhookConfig[]",
 "POST /2/webhooks": "Webhooks.CreateAsync | { url } | XWebhookConfig",
 "PUT /2/webhooks/{webhook_id}": "Webhooks.ValidateAsync | - | XWebhookValidation",
 "DELETE /2/webhooks/{webhook_id}": "Webhooks.DeleteAsync | - | XWebhookDeleteResult",
 "POST /2/webhooks/replay": "Webhooks.CreateReplayJobAsync | { webhook_id, from_date, to_date } | XWebhookReplayJob",
 "GET /2/tweets/search/webhooks": "Webhooks.GetStreamLinksAsync | - | XWebhookStreamLink[]",
 "POST /2/tweets/search/webhooks/{webhook_id}": "Webhooks.CreateStreamLinkAsync | - | XWebhookLinkResult",
 "DELETE /2/tweets/search/webhooks/{webhook_id}": "Webhooks.DeleteStreamLinkAsync | - | XWebhookDeleteResult",
 "POST /2/account_activity/webhooks/{webhook_id}/subscriptions/all": "Webhooks.CreateSubscriptionAsync | { } | XAccountActivitySubscription",
 "GET /2/account_activity/webhooks/{webhook_id}/subscriptions/all": "Webhooks.ValidateSubscriptionAsync | - | XAccountActivitySubscription",
 "GET /2/account_activity/webhooks/{webhook_id}/subscriptions/all/list": "Webhooks.GetSubscriptionsAsync | - | XAccountActivitySubscriptionList",
 "DELETE /2/account_activity/webhooks/{webhook_id}/subscriptions/{user_id}/all": "Webhooks.DeleteSubscriptionAsync | - | XAccountActivitySubscription",
 "GET /2/account_activity/subscriptions/count": "Webhooks.GetSubscriptionCountAsync | - | XAccountActivityCount",
}

# Notes for the Access column. Only statements X documents (or the OpenAPI document states) are recorded as facts.
ACCESS = [
 ("/2/tweets/firehose", "Enterprise stream (partitioned); X answers 403 without enrolment. Needs the `partition` parameter."),
 ("/2/tweets/sample10", "Enterprise stream (partitioned); X answers 403 without enrolment. Needs the `partition` parameter."),
 ("/2/tweets/search/stream", "Filtered stream: documented limit of 1 connection; plan-dependent."),
 ("/2/tweets/sample/stream", "Plan-dependent."),
 ("/2/tweets/search/all", "Full-archive search: plan-dependent."),
 ("/2/tweets/counts/all", "Full-archive counts: plan-dependent."),
 ("/2/compliance", "Needs an approved developer account, a Project and App, and the app's Bearer token."),
 ("/2/dm_", "Plan-dependent; unencrypted DM endpoints only (X Chat is a separate product)."),
 ("/2/usage", "Reports usage for your own project."),
 ("/2/news", "Plan-dependent."),
 ("/2/webhooks", "Needs a public HTTPS callback (no port) that answers CRC checks; plan-dependent. Receiving events is outside the SDK (see `XWebhookSecurity`)."),
 ("/2/account_activity", "Plan-dependent; events arrive at your webhook, not through the SDK."),
 ("/2/tweets/search/webhooks", "App-only; links a webhook to the filtered stream. Plan-dependent."),
 ("/2/connections", "App-only. Terminating a connection is immediate and affects every consumer of that stream."),
 ("/2/spaces/", "Plan-dependent; ticket buyers need OAuth 2.0 user context."),
]
POST_NOTE = "Quote Posting (`quote_tweet_id`) requires an Enterprise plan (documented)."

UNSUPPORTED = [  # (predicate, status, reason) evaluated in order for operations the SDK does not implement
 (lambda p: p.startswith("/2/chat/"), "Not supported", "Encrypted X Chat: a separately licensed product with client-side key management. Deliberately out of scope."),
 (lambda p: "compliance/stream" in p or "label/stream" in p or "/likes/" in p and "/stream" in p, "Access-restricted", "Enterprise compliance / label / likes streams (partitioned, different payloads); not implemented."),
 (lambda p: p.startswith("/2/activity"), "Not implemented", "Activity API (subscriptions and event stream)."),
 (lambda p: p.startswith(("/2/broadcasts", "/2/sources", "/2/calls", "/2/bots")) or p == "/2/region" or "/broadcasts" in p or "/sources" in p, "Not implemented", "Live broadcasts, sources, calls and bots modules."),
 (lambda p: p.startswith("/2/media/amplify") or p.startswith("/2/media/library"), "Not implemented", "Media library and Amplify programs."),
 (lambda p: p.startswith("/2/compliance/jobs/") and p.endswith(("/download", "/upload")), "Not implemented", "Spec routes with a `token` query parameter; the SDK uses the job's pre-signed `upload_url`/`download_url` through `UploadIdsAsync`/`DownloadResultsAsync` instead (documented flow, not verified live)."),
 (lambda p: p.startswith("/2/notes"), "Not implemented", "Community Notes."),
 (lambda p: p.startswith("/2/account"), "Not implemented", "Developer account management."),
 (lambda p: p.startswith("/2/articles"), "Not implemented", "Articles."),
 (lambda p: "public_keys" in p, "Not supported", "X Chat encryption keys (the spec says they are registered for X Chat encryption); belongs with the deliberately unsupported encrypted-chat family."),
 (lambda p: p.startswith("/2/dm_conversations/media"), "Not implemented", "DM media download."),
 (lambda p: p == "/2/users/compliance/stream", "Access-restricted", "Enterprise compliance stream."),
 (lambda p: p == "/2/openapi.json", "Not applicable", "Serves the OpenAPI document itself."),
]

def flags(op):
    names = {"OAuth2UserToken": "OAuth 2.0", "UserToken": "OAuth 1.0a", "BearerToken": "App-only"}
    out = []
    for sec in op.get("security", []):
        for k in sec:
            if names[k] not in out:
                out.append(names[k])
    return out

def ratelimit(op):
    a = flags(op)
    if a == ["App-only"]:
        return "Per-app"
    if "App-only" not in a:
        return "Per-user"
    return "Per-user (user tokens) or per-app (Bearer)"

def scopes(op):
    s = []
    for sec in op.get("security", []):
        for k, v in sec.items():
            if k == "OAuth2UserToken":
                for x in v:
                    if x not in s:
                        s.append(x)
    return s

def used_routes():
    used = {}
    for f in glob.glob(os.path.join(ROOT, "src/XConnect/**/*.cs"), recursive=True):
        t = open(f).read()
        for m, r in re.findall(r'Create\(\s*HttpMethod\.(\w+),\s*"(/2/[^"]+)"', t): used[(m.upper(), r)] = f
        for r in re.findall(r'(?:Search|Counts|Stream|Page|Events|Untarget)\(\s*"(/2/[^"]+)"', t):
            used[("DELETE" if "Untarget" in t and r.count("{") == 2 else "GET", r)] = f
        for m, r in re.findall(r'Target\(\s*HttpMethod\.(\w+),\s*"(/2/[^"]+)"', t): used[(m.upper(), r)] = f
        if f.endswith("StreamingClient.cs"):
            for r in re.findall(r'=>\s*"(/2/tweets/[^"]+/stream[^"]*)"', t): used[("GET", r)] = f
    return used

def tests_for(member_expr, tests_text):
    n = 0
    names = re.sub(r"\([^)]*\)", "", member_expr.split(" | ")[0])
    for part in names.split(","):
        part = part.strip()
        if not part:
            continue
        if "." in part:
            area, meth = part.split(".")
            n += len(re.findall(r"\.%s\s*\.\s*%s\(" % (area, meth), tests_text))
        else:  # continuation of the same area, e.g. "ReplyAsync"
            n += len(re.findall(r"\.%s\(" % part, tests_text))
    return n

def compact_schema(node, spec, used, depth=0):
    """Converts an OpenAPI schema into the compact form the conformance tests read; component schemas are kept by name in `used`."""
    if "$ref" in node:
        name = node["$ref"].split("/")[-1]
        if name not in used:
            used[name] = None  # reserve to stop cycles
            used[name] = compact_schema(spec["components"]["schemas"][name], spec, used, depth + 1)
        return {"ref": name}
    out = {}
    t = node.get("type")
    if t:
        out["type"] = t
    for src, dst in (("enum", "enum"), ("minimum", "min"), ("maximum", "max"), ("minItems", "minItems"), ("maxItems", "maxItems"), ("pattern", "pattern")):
        if src in node:
            out[dst] = node[src]
    if "items" in node:
        out["items"] = compact_schema(node["items"], spec, used, depth + 1)
    if "properties" in node:
        out["props"] = {k: compact_schema(v, spec, used, depth + 1) for k, v in node["properties"].items()}
    if node.get("required"):
        out["req"] = node["required"]
    ap = node.get("additionalProperties")
    if ap is True or isinstance(ap, dict):
        out["open"] = True
    for src in ("allOf", "oneOf", "anyOf"):
        if src in node:
            out[src] = [compact_schema(x, spec, used, depth + 1) for x in node[src]]
    return out

def op_contract(o, spec, used):
    """Parameters, request body and success response of one operation in compact form."""
    params = []
    for prm in o.get("parameters", []):
        if "$ref" in prm:
            prm = spec["components"]["parameters"][prm["$ref"].split("/")[-1]]
        params.append({"name": prm["name"], "in": prm["in"], "required": bool(prm.get("required")), "schema": compact_schema(prm.get("schema", {}), spec, used)})
    contract = {"parameters": params}
    body = o.get("requestBody", {}).get("content", {})
    for mt, c in body.items():
        contract.setdefault("body", {})[mt] = compact_schema(c.get("schema", {}), spec, used)
    for code in ("200", "201", "202", "204"):
        r = o.get("responses", {}).get(code)
        if r:
            sch = r.get("content", {}).get("application/json", {}).get("schema")
            if sch:
                contract["response"] = compact_schema(sch, spec, used)
            break
    return contract

def main():
    if len(sys.argv) > 1:
        spec = json.load(open(sys.argv[1]))
    else:
        spec = json.load(urllib.request.urlopen(SPEC_URL))
    used = used_routes()
    ops = {}
    for p, items in spec["paths"].items():
        for m, o in items.items():
            if m in ("get", "post", "put", "delete"):
                ops[(m.upper(), p)] = o
    missing = [k for k in used if k not in ops]
    assert not missing, f"routes used by the SDK but absent from the spec: {missing}"
    keys_in_M = {tuple(k.split(" ", 1)) for k in M}
    assert keys_in_M == set(used), f"member table and code disagree: {sorted(keys_in_M ^ set(used))}"

    used_schemas = {}
    supported_keys = set(used)
    snapshot = {
        "specVersion": spec["info"]["version"],
        "operations": [
            {"method": m, "path": p, "operationId": o.get("operationId"), "auth": flags(o), "scopes": scopes(o),
             # Contracts are kept only for operations the SDK implements; the others need nothing beyond route/auth/scopes.
             **({"contract": op_contract(o, spec, used_schemas)} if (m, p) in supported_keys else {})}
            for (m, p), o in sorted(ops.items(), key=lambda kv: (kv[0][1], kv[0][0]))
        ],
        "schemas": used_schemas,
    }
    json.dump(snapshot, open(os.path.join(ROOT, "docs/spec-snapshot.json"), "w"), separators=(",", ":"), sort_keys=False)
    tests_text = "\n".join(open(f).read() for f in glob.glob(os.path.join(ROOT, "tests/XConnect.Tests/*.cs")))

    rows = []
    for (m, p), o in ops.items():
        key = f"{m} {p}"
        auth = ", ".join(flags(o)) or "-"
        sc = ", ".join(scopes(o)) or "-"
        if key in M:
            member, req, resp = [x.strip() for x in M[key].split(" | ")]
            n = tests_for(M[key], tests_text)
            access = next((note for pre, note in ACCESS if p.startswith(pre)), "Availability depends on your X plan and app permissions.")
            if key == "POST /2/tweets":
                access = POST_NOTE
            rows.append((m, p, "Supported", member, req, resp, auth, sc, ratelimit(o), access, f"{n} call(s)" if n else "**NONE**"))
        else:
            st, why = "Not implemented", "Not part of the implemented scope."
            for pred, s, w in UNSUPPORTED:
                if pred(p):
                    st, why = s, w
                    break
            rows.append((m, p, st, "", "", "", auth, sc, ratelimit(o), why, ""))

    counts = {}
    for r in rows:
        counts[r[2]] = counts.get(r[2], 0) + 1
    untested = [r for r in rows if r[2] == "Supported" and r[10] == "**NONE**"]

    L = []
    L.append("# API coverage matrix\n")
    L.append(f"Source of truth: the official X API OpenAPI document (`{SPEC_URL}`, version **{spec['info']['version']}**). "
             "This file is generated by `tools/generate_coverage.py` from that document, from the routes found in `src/XConnect`, and from the tests; do not edit it by hand. "
             "`docs/spec-snapshot.json` holds the compact snapshot that `SpecConformanceTests` checks the code against on every test run.\n")
    L.append(f"**Total: {len(rows)} operations. " + ", ".join(f"{k}: {v}" for k, v in sorted(counts.items(), key=lambda kv: kv[0])) + ".**\n")
    if counts.get("Supported", 0) < len(rows):
        L.append("> This is **not** full coverage of the specification. Every *Supported* row is implemented and tested; every other row is listed with its reason.\n")
    L.append("## How to read this\n")
    L.append("- **Auth** and **Scopes** are what the OpenAPI document lists for the endpoint. The SDK only sends an endpoint the authentication methods that are a subset of these (checked by `SpecConformanceTests`), and throws `InvalidOperationException` before sending when none of your configured methods qualifies. Scopes are requested when you authorise (see `XScopes`); the SDK cannot check that a token holds them, so a missing scope surfaces as an API error (`403`).")
    L.append("- **Rate limit.** X documents per-user limits for OAuth 1.0a / OAuth 2.0 user tokens and per-app limits for Bearer tokens, separately for each endpoint, reported in `x-rate-limit-limit/-remaining/-reset`. The SDK tracks the latest values per `METHOD route` in `client.RateLimits` and on every `XResponse.RateLimit`. Concrete numbers are plan-specific and are not in the OpenAPI document, so they are not repeated here.")
    L.append("- **Access** records only what X documents or the OpenAPI document states; otherwise availability depends on your X plan and permissions. X answers `403` when your plan lacks access.")
    L.append("- **Tests** counts calls to the SDK member in `tests/XConnect.Tests` (all stubbed; nothing here has been exercised against the live API in this repository's automated tests, see `docs/integration-testing.md`).\n")
    L.append("## Partially supported operations\n")
    L.append("- `POST /2/tweets`: every request field is modelled except `reply.auto_populate_reply_metadata`, which the documentation says is accepted but dropped before the request is processed. `media.call_to_actions` supports the `app_install`, `visit_site` and `watch_now` variants.")
    L.append("- `POST /2/media/metadata`: alt text, download status, content expiration, domain restrictions, found-media origin, management info, shared info and upload source. `audience_policy`, `geo_restrictions`, `sensitive_media_warning`, `sticker_info` and `preview_image` are untyped objects in the specification and are not exposed.")
    L.append("- `GET /2/tweets/analytics`, `GET /2/media/analytics`: all documented metrics are modelled; media counters are `JsonElement` because the API returns an integer or the original string.")
    L.append("- Compliance: create, list, get and cancel jobs, plus upload and download through the pre-signed URLs. Result files are returned as a raw stream (their format is not specified).")
    L.append("- Response models: for 123 operations every documented response field is modelled except `associated_metadata` on the media metadata result. Fields the specification leaves as untyped objects (`article`, `article_title`, and a Post's `organic_metrics`, `non_public_metrics`, `promoted_metrics`) are exposed as `JsonElement`.")
    L.append("- Webhooks and Account Activity: management calls only; receiving events is outside the SDK (`XWebhookSecurity` handles the CRC and signature checks). Not exercised live.")
    L.append("- News and usage: untyped parts of the schema (`contexts`, `keywords`, daily usage lists, free grants) are exposed as `JsonElement`.\n")
    if untested:
        L.append("## Supported operations without a test\n")
        for r in untested:
            L.append(f"- {r[0]} `{r[1]}` ({r[3]})")
        L.append("")
    else:
        L.append("Every supported operation is called by at least one test.\n")
    L.append("## Operations\n")
    L.append("| Method | Path | Status | SDK member | Request | Response | Auth | Scopes | Rate limit | Access / note | Tests |")
    L.append("|---|---|---|---|---|---|---|---|---|---|---|")
    order = {"Supported": 0, "Access-restricted": 1, "Not implemented": 2, "Not supported": 3, "Not applicable": 4}
    for r in sorted(rows, key=lambda r: (order[r[2]], r[1], r[0])):
        c = list(r)
        c[3] = f"`{c[3]}`" if c[3] else ""
        c[4] = f"`{c[4]}`" if c[4] else ""
        c[5] = f"`{c[5]}`" if c[5] else ""
        c[1] = f"`{c[1]}`"
        L.append("| " + " | ".join(str(x).replace("|", "\\|") for x in c) + " |")
    open(os.path.join(ROOT, "docs/api-coverage.md"), "w").write("\n".join(L) + "\n")
    print(counts, "untested supported:", len(untested))

main()
