using System.Security.Cryptography;
using System.Text;

namespace XConnect;

/// <summary>
/// Builds OAuth 1.0a (HMAC-SHA1) <c>Authorization</c> headers as described in the X API documentation.
/// JSON request bodies are not part of the signature; only OAuth and query parameters are.
/// </summary>
internal static class OAuth1Signer
{
    public static string CreateAuthorizationHeader(
        HttpMethod method,
        Uri url,
        XCredentials credentials,
        string nonce,
        long timestamp)
    {
        var oauthParameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_consumer_key"] = credentials.ConsumerKey,
            ["oauth_nonce"] = nonce,
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["oauth_token"] = credentials.AccessToken,
            ["oauth_version"] = "1.0",
        };

        var signature = Sign(method, url, oauthParameters, credentials);

        var header = new StringBuilder("OAuth ");
        foreach (var (key, value) in oauthParameters.Append(KeyValuePair.Create("oauth_signature", signature))
                                                    .OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (header.Length > "OAuth ".Length)
            {
                header.Append(", ");
            }

            header.Append(Encode(key)).Append("=\"").Append(Encode(value)).Append('"');
        }

        return header.ToString();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "HMAC-SHA1 is mandated by the OAuth 1.0a protocol used by the X API.")]
    internal static string Sign(
        HttpMethod method,
        Uri url,
        IReadOnlyDictionary<string, string> oauthParameters,
        XCredentials credentials)
    {
        // Query-string parameters take part in the signature; the base URL does not include them.
        var allParameters = new List<KeyValuePair<string, string>>(oauthParameters);
        allParameters.AddRange(ParseQuery(url.Query));

        var parameterString = string.Join('&', allParameters
            .Select(p => (Key: Encode(p.Key), Value: Encode(p.Value)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}"));

        var baseUrl = url.GetLeftPart(UriPartial.Path);
        var baseString = $"{method.Method.ToUpperInvariant()}&{Encode(baseUrl)}&{Encode(parameterString)}";
        var signingKey = $"{Encode(credentials.ConsumerSecret)}&{Encode(credentials.AccessTokenSecret)}";

        var hash = HMACSHA1.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(baseString));
        return Convert.ToBase64String(hash);
    }

    /// <summary>RFC 3986 percent-encoding: only unreserved characters are left as-is.</summary>
    internal static string Encode(string value)
    {
        var builder = new StringBuilder(value.Length * 2);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if (b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9')
                or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~')
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=');
            var key = index < 0 ? pair : pair[..index];
            var value = index < 0 ? string.Empty : pair[(index + 1)..];
            // RFC 5849 3.4.1.3.1: query parameters are form-decoded, so "+" means a space.
            yield return KeyValuePair.Create(Uri.UnescapeDataString(key.Replace('+', ' ')), Uri.UnescapeDataString(value.Replace('+', ' ')));
        }
    }
}
