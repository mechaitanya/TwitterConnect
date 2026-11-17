# Advertising API (evaluation)

The [X Ads API](https://docs.x.com/x-ads-api/introduction) manages ad campaigns, custom audiences, creatives, lead generation and advertising
analytics. It is **not part of the X API v2 OpenAPI document** that XConnect is built from (that document contains no ads endpoints),
and it is not exposed through XConnect.

What the documentation states:

- Access is **approval-based**: you submit the Ads API Access Form, and review can take up to three business days.
- Its endpoints are OAuth-authenticated. The introduction page does not say which OAuth version; confirm in the reference before building against it.
- It requires an advertising account, and what you can do depends on that account and on the approval you receive.

Decision: **out of scope for this package.** Implementing it would mean a second authentication and permission model, a separate resource hierarchy
(accounts, campaigns, line items, audiences) and different versioning from the rest of the SDK, and it cannot be tested without an approved advertising
account. It should be a separate, clearly labelled module (or package) when there is a concrete need. Do not treat anything in XConnect as providing
advertising functionality.
