using System.Runtime.CompilerServices;
using XConnect;
using XConnect.Authentication;

[assembly: InternalsVisibleTo("QuickStart.Tests")]

namespace QuickStart;

/// <summary>
/// User-context stand-in used only against a simulator. The X API Playground cannot tell an OAuth 2.0 user token from an app-only
/// token, so it needs the nonstandard <c>X-Auth-Method: oauth2</c> header. The SDK only uses this authenticator for endpoints that
/// require a user context (such as <c>/2/users/me</c>), so other requests never carry the header. It is never added for the real X API.
/// </summary>
internal sealed class SimulatorUserAuthenticator(string token) : IXAuthenticator
{
    public XAuthMethods Method => XAuthMethods.OAuth2User;

    public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("X-Auth-Method", "oauth2");
        return ValueTask.CompletedTask;
    }
}

internal static class QuickStartApp
{
    internal const string Usage = "Usage: QuickStart [<username> | --id <user-id> | --me]   (--me works only against a simulator)";

    public static XConnectOptions BuildOptions(QuickStartConfig config)
    {
        var options = new XConnectOptions();
        if (config.BaseAddress is not null)
        {
            options.BaseAddress = config.BaseAddress;
        }

        if (config.BearerToken is { } token)
        {
            options.UseBearerToken(token);
            if (config.IsSimulator)
            {
                options.UseAuthenticator(new SimulatorUserAuthenticator(token));
            }
        }

        return options;
    }

    public static async Task<int> RunAsync(string[] args, QuickStartConfig config, HttpMessageHandler? handler, TextWriter output)
    {
        if (config.BearerToken is null)
        {
            await output.WriteLineAsync("No bearer token. Put it in samples/QuickStart/appsettings.Local.json (XConnect:BearerToken) or set XCONNECT_BEARER_TOKEN.");
            return 1;
        }

        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        var client = new XConnectClient(http, BuildOptions(config));

        if (config.IsSimulator)
        {
            await output.WriteLineAsync($"SIMULATOR at {config.BaseAddress}: responses are simulated and are not real X API responses.");
        }

        XResponse<XConnect.Models.XUser> response;
        if (args is ["--me"])
        {
            if (!config.IsSimulator)
            {
                await output.WriteLineAsync("--me needs a user-context credential, which this sample only provides for a simulator. " + Usage);
                return 1;
            }

            response = await client.Users.GetMeAsync();
        }
        else if (args is ["--id", var id])
        {
            response = await client.Users.GetByIdAsync(id);
        }
        else if (args.Length <= 1 && args.FirstOrDefault()?.StartsWith("--", StringComparison.Ordinal) != true)
        {
            response = await client.Users.GetByUsernameAsync(args.Length > 0 ? args[0].TrimStart('@') : "XDevelopers");
        }
        else
        {
            await output.WriteLineAsync(Usage);
            return 1;
        }

        if (response.IsSuccess && response.Data is null)
        {
            // X reports an unknown user as HTTP 200 with an errors array and no data.
            var reason = response.PartialErrors.Count > 0 ? response.PartialErrors[0].Message : "no user returned";
            await output.WriteLineAsync($"No user found: {reason}");
            return 2;
        }

        if (response.IsSuccess)
        {
            var user = response.Data!;
            await output.WriteLineAsync($"{user.Name} (@{user.Username}), id {user.Id}");
            return 0;
        }

        var error = response.Error!;
        if (error.Kind == XApiErrorKind.NotFound)
        {
            await output.WriteLineAsync($"No user found: {error.Message} (HTTP {(int)error.StatusCode})");
            return 2;
        }

        await output.WriteLineAsync($"{error.Kind} (HTTP {(int)error.StatusCode}): {error.Message}");
        if (error.Kind == XApiErrorKind.PaymentRequired)
        {
            await output.WriteLineAsync(error.IsCreditsDepleted
                ? "The X app has no credits. Add credits in the X developer console, then run again."
                : "The account needs a plan or payment for this endpoint.");
        }
        else if (error.Kind == XApiErrorKind.Authentication)
        {
            await output.WriteLineAsync("The bearer token was rejected. Check it or regenerate it.");
        }

        return 2;
    }
}
