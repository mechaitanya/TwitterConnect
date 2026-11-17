using System.Security.Cryptography;
using System.Text;

namespace XConnect;

/// <summary>
/// Helpers for the receiving side of X webhooks, following X's documented scheme: both the CRC <c>response_token</c> and the request signature are
/// <c>sha256=</c> followed by the Base64 HMAC-SHA256 (of the <c>crc_token</c> or of the raw request body) keyed with your app's OAuth 2.0 client
/// secret (recommended; header <c>X-Twitter-Webhooks-Signature-OAuth2</c>) or its OAuth 1.0 consumer secret (legacy; <c>X-Twitter-Webhooks-Signature</c>).
/// </summary>
/// <remarks>
/// The bearer token used to call <c>/2/webhooks</c> is never the key. Verify the signature over the exact bytes received, before parsing them.
/// </remarks>
public static class XWebhookSecurity
{
    /// <summary>The prefix both the CRC response token and the signature header carry.</summary>
    public const string Prefix = "sha256=";

    /// <summary>Computes the <c>response_token</c> to return for a CRC challenge (<c>{"response_token":"..."}</c>).</summary>
    public static string CreateCrcResponseToken(string crcToken, string secret)
    {
        ArgumentNullException.ThrowIfNull(crcToken);
        return Sign(Encoding.UTF8.GetBytes(crcToken), secret);
    }

    /// <summary>Computes the signature header value for a payload.</summary>
    public static string ComputeSignature(ReadOnlySpan<byte> body, string secret) => Sign(body, secret);

    /// <summary>Checks a received signature header against the raw body in constant time. Returns <see langword="false"/> for a missing or malformed header.</summary>
    public static bool VerifySignature(ReadOnlySpan<byte> body, string? signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(body, secret));
        var actual = Encoding.ASCII.GetBytes(signatureHeader);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string Sign(ReadOnlySpan<byte> data, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data, hash);
        return Prefix + Convert.ToBase64String(hash);
    }
}
