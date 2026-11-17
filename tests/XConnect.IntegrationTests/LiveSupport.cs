using System.Text.Json;
using Xunit.Abstractions;
using XConnect.Authentication;

namespace XConnect.IntegrationTests;

/// <summary>
/// Everything here talks to the real X API, so every test is skipped unless you opt in. Credentials come from environment variables only;
/// nothing is read from or written to the repository, and nothing secret is ever written to test output.
/// </summary>
internal static class Live
{
    /// <summary>The exact phrase that authorises tests which create (and delete) real Posts or upload media.</summary>
    public const string WritePhrase = "CREATE-AND-DELETE-A-REAL-POST";

    public static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    public static bool Enabled => Env("XCONNECT_LIVE") == "1";

    public static bool WritesAuthorised => Env("XCONNECT_LIVE_ALLOW_WRITES") == WritePhrase;

    /// <summary>Phrase-free opt-in for tests that rotate the stored OAuth 2.0 token (the refresh token may be single-use).</summary>
    public static bool RefreshAuthorised => Env("XCONNECT_LIVE_ALLOW_TOKEN_REFRESH") == "1";

    /// <summary>Returns why a test must be skipped, or <see langword="null"/> when it may run.</summary>
    public static string? SkipReason(bool writes, params string[] required) => SkipReason(Env, writes, false, required);

    /// <summary>The gate logic with an injectable environment so it can be unit-tested without touching the process environment.</summary>
    internal static string? SkipReason(Func<string, string?> env, bool writes, bool rotatesTokens, string[] required)
    {
        if (env("XCONNECT_LIVE") != "1")
        {
            return "Live tests are opt-in: set XCONNECT_LIVE=1 (see docs/integration-testing.md).";
        }

        if (writes && env("XCONNECT_LIVE_ALLOW_WRITES") != WritePhrase)
        {
            return $"This test creates real content. Set XCONNECT_LIVE_ALLOW_WRITES={WritePhrase} to authorise it.";
        }

        if (rotatesTokens && env("XCONNECT_LIVE_ALLOW_TOKEN_REFRESH") != "1")
        {
            return "This test refreshes (and may rotate) the stored OAuth 2.0 token. Set XCONNECT_LIVE_ALLOW_TOKEN_REFRESH=1 to authorise it.";
        }

        var missing = required.Where(r => string.IsNullOrEmpty(env(r))).ToArray();
        return missing.Length == 0 ? null : "Missing environment variable(s): " + string.Join(", ", missing);
    }

    /// <summary>Remembers the Posts this run created so deletion can never touch anything else.</summary>
    internal sealed class PostLedger(XConnectClient client, ITestOutputHelper output)
    {
        private readonly HashSet<string> _created = [];

        public void Record(string id) => _created.Add(id);

        public bool Owns(string id) => _created.Contains(id);

        /// <summary>Deletes a Post only if this run created it; anything else throws before a request is made.</summary>
        public async Task<bool> DeleteAsync(string id)
        {
            if (!Owns(id))
            {
                throw new InvalidOperationException($"Refusing to delete Post {id}: it was not created by this test run.");
            }

            var deleted = await client.Posts.DeleteAsync(id, CancellationToken.None);
            output.WriteLine($"Deleted Post {id}: {deleted.Data?.Deleted} (HTTP {(int)deleted.StatusCode}).");
            return deleted.Data?.Deleted == true;
        }
    }

    public static readonly string[] OAuth1Variables =
    [
        "XCONNECT_CONSUMER_KEY", "XCONNECT_CONSUMER_SECRET", "XCONNECT_ACCESS_TOKEN", "XCONNECT_ACCESS_TOKEN_SECRET",
    ];

    public static XCredentials OAuth1() => new(
        Env("XCONNECT_CONSUMER_KEY")!, Env("XCONNECT_CONSUMER_SECRET")!, Env("XCONNECT_ACCESS_TOKEN")!, Env("XCONNECT_ACCESS_TOKEN_SECRET")!);

    public static XOAuth2Options OAuth2Options() => new()
    {
        ClientId = Env("XCONNECT_OAUTH2_CLIENT_ID")!,
        ClientSecret = Env("XCONNECT_OAUTH2_CLIENT_SECRET"),
        RedirectUri = Env("XCONNECT_OAUTH2_REDIRECT_URI") is { } uri ? new Uri(uri) : null,
    };

    /// <summary>Writes a file that only the current user can read, because it holds a token.</summary>
    public static void WriteSecretFile(string path, string contents)
    {
        File.WriteAllText(path, contents);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public static string Serialize(XOAuth2Token token) => JsonSerializer.Serialize(new StoredToken(token.AccessToken, token.RefreshToken, token.ExpiresAt, [.. token.Scopes]));

    public static XOAuth2Token Deserialize(string json)
    {
        var stored = JsonSerializer.Deserialize<StoredToken>(json) ?? throw new InvalidOperationException("The token file is empty.");
        return new XOAuth2Token { AccessToken = stored.AccessToken, RefreshToken = stored.RefreshToken, ExpiresAt = stored.ExpiresAt, Scopes = stored.Scopes };
    }

    private sealed record StoredToken(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string[] Scopes);
}

/// <summary>A fact that is skipped unless the live environment is configured for it.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
    /// <param name="writes">True when the test creates real content and needs the explicit write authorisation.</param>
    /// <param name="required">Environment variables the test needs.</param>
    public LiveFactAttribute(bool writes = false, params string[] required) => Skip = Live.SkipReason(writes, required);

    /// <summary>For tests that refresh the OAuth 2.0 token and so need <c>XCONNECT_LIVE_ALLOW_TOKEN_REFRESH=1</c> as well.</summary>
    public LiveFactAttribute(string[] required, bool rotatesTokens) => Skip = Live.SkipReason(Live.Env, false, rotatesTokens, required);
}

/// <summary>Variants kept as constants so attribute arguments stay readable.</summary>
internal static class Vars
{
    public static readonly string[] OAuth1 = Live.OAuth1Variables;
}
