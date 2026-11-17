using System.Text.Json;

namespace QuickStart;

/// <summary>Raised for a configuration value that cannot be used. The message names the setting and never contains a secret.</summary>
internal sealed class QuickStartConfigException(string message) : Exception(message);

/// <summary>
/// Settings for the sample. Activation rules (nothing is inferred from the host name):
/// <list type="bullet">
/// <item><c>XCONNECT_BASE_ADDRESS</c> unset: real X API, real bearer token.</item>
/// <item><c>XCONNECT_BASE_ADDRESS</c> set to <c>https://api.x.com</c>: the same as unset.</item>
/// <item><c>XCONNECT_BASE_ADDRESS</c> set to any other host: refused unless <c>XCONNECT_SIMULATOR=1</c> is also set, so neither the real
/// token nor the placeholder is ever sent to an unintended proxy, staging host or other API.</item>
/// <item><c>XCONNECT_SIMULATOR=1</c> with a custom address: simulator mode. The real token is never sent; it uses
/// <c>XCONNECT_PLAYGROUND_TOKEN</c> or the placeholder.</item>
/// <item><c>XCONNECT_SIMULATOR=1</c> without an address, or with the real host: refused.</item>
/// </list>
/// </summary>
internal sealed record QuickStartConfig(Uri? BaseAddress, string? BearerToken, bool IsSimulator)
{
    /// <summary>The real X API host. A base address pointing here is treated as the real API, not as a simulator.</summary>
    internal const string RealApiHost = "api.x.com";

    /// <summary>The placeholder token the X API Playground accepts. It is not a secret.</summary>
    internal const string PlaygroundDefaultToken = "test_token";

    /// <summary>Parses <c>XCONNECT_SIMULATOR</c>: unset, blank, <c>0</c> or <c>false</c> is off; <c>1</c> or <c>true</c> is on; anything else is an error.</summary>
    public static bool ParseSimulatorFlag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() is "0" || value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Trim() is "1" || value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new QuickStartConfigException("XCONNECT_SIMULATOR must be 1 or true to enable simulator mode (or unset).");
    }

    /// <summary>Validates the optional base address: unset or blank gives <see langword="null"/>; otherwise it must be an absolute http or https URI.</summary>
    public static Uri? ParseBaseAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new QuickStartConfigException("XCONNECT_BASE_ADDRESS must be an absolute http or https URL, for example http://127.0.0.1:18080.");
        }

        // A trailing slash makes relative request paths resolve under the configured path.
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }

    /// <summary>Reads the settings. <paramref name="environment"/> overrides <paramref name="file"/> for the real bearer token.</summary>
    public static QuickStartConfig Load(Func<string, string?> environment, Func<string, string?> file)
    {
        var baseAddress = ParseBaseAddress(environment("XCONNECT_BASE_ADDRESS"));
        var simulatorRequested = ParseSimulatorFlag(environment("XCONNECT_SIMULATOR"));
        var isRealHost = baseAddress is not null && string.Equals(baseAddress.Host, RealApiHost, StringComparison.OrdinalIgnoreCase);

        if (isRealHost && baseAddress!.Scheme != Uri.UriSchemeHttps)
        {
            throw new QuickStartConfigException("XCONNECT_BASE_ADDRESS for api.x.com must use https so the bearer token is never sent in clear text.");
        }

        if (simulatorRequested && (baseAddress is null || isRealHost))
        {
            throw new QuickStartConfigException("XCONNECT_SIMULATOR=1 needs XCONNECT_BASE_ADDRESS set to the simulator (not api.x.com), for example http://127.0.0.1:18080.");
        }

        if (baseAddress is not null && !isRealHost && !simulatorRequested)
        {
            throw new QuickStartConfigException(
                $"XCONNECT_BASE_ADDRESS points at {baseAddress.Host}, which is not api.x.com. Set XCONNECT_SIMULATOR=1 to confirm it is a simulator; the sample will not send your real token to another host.");
        }

        // Never send the real token to a custom address.
        var token = simulatorRequested
            ? Pick(environment("XCONNECT_PLAYGROUND_TOKEN")) ?? PlaygroundDefaultToken
            : Pick(environment("XCONNECT_BEARER_TOKEN")) ?? Pick(file("BearerToken"));
        return new QuickStartConfig(baseAddress, token, simulatorRequested);
    }

    /// <summary>Reads <c>XConnect:{name}</c> from appsettings.Local.json, then appsettings.json, next to the executable.</summary>
    public static string? ReadSettingFile(string name)
    {
        foreach (var fileName in new[] { "appsettings.Local.json", "appsettings.json" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("XConnect", out var section)
                && section.TryGetProperty(name, out var value)
                && value.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private static string? Pick(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
