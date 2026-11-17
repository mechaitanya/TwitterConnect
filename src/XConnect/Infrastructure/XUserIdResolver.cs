using XConnect.Authentication;
using XConnect.Serialization;

namespace XConnect.Infrastructure;

/// <summary>Resolves, and caches, the authenticated user's ID for routes that require it.</summary>
internal sealed class XUserIdResolver(XApiTransport transport)
{
    private string? _userId;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        if (transport.Options.AuthenticatedUserId is { Length: > 0 } configured)
        {
            return configured;
        }

        if (Volatile.Read(ref _userId) is { } cached)
        {
            return cached;
        }

        // Two concurrent first calls may both look the ID up; that is harmless and avoids holding a lock across a request.
        var me = await transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/users/me", XAuthMethods.UserContext),
            XConnectJsonContext.Default.EnvelopeXUser,
            cancellationToken).ConfigureAwait(false);

        var id = me.GetDataOrThrow().Id
            ?? throw new XApiException(
                "X did not return the authenticated user's ID.",
                new XApiError(XApiErrorKind.Serialization, me.StatusCode, null, null, "Missing user ID.", null, me.RequestId));
        Volatile.Write(ref _userId, id);
        return id;
    }
}
