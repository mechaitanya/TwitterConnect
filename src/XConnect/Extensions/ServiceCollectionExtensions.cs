using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XConnect.Infrastructure;

namespace XConnect;

/// <summary>Registration helpers for dependency injection.</summary>
public static class ServiceCollectionExtensions
{
    private const string OAuth2HttpClientName = "XConnect.OAuth2";

    /// <summary>
    /// Registers <see cref="IXConnectClient"/> backed by <see cref="IHttpClientFactory"/>, with validated options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets authentication and other options.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for the underlying client, so you can add handlers such as a custom <see cref="HttpMessageHandler"/>.</returns>
    /// <example>
    /// <code>
    /// services.AddXConnect(options =>
    /// {
    ///     options.BaseAddress = new Uri("https://api.x.com/");
    ///     options.UseOAuth1(credentials);
    /// });
    /// </code>
    /// </example>
    public static IHttpClientBuilder AddXConnect(this IServiceCollection services, Action<XConnectOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<XConnectOptions>().Configure(configure);
        services.AddSingleton<IValidateOptions<XConnectOptions>, XConnectOptionsValidator>();
        services.AddOptions<XConnectOptions>().ValidateOnStart();
        services.TryAddTimeProvider();

        // Typed HttpClient registrations are transient. Authentication state (so a single-use OAuth 2.0 refresh token is spent once)
        // and rate-limit tracking must be shared by every injected client, so they live in one singleton.
        // OAuth 2.0 token requests use the named client "XConnect.OAuth2", which you can configure with services.AddHttpClient("XConnect.OAuth2").
        services.AddSingleton(sp => XConnectShared.Create(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OAuth2HttpClientName),
            sp.GetRequiredService<IOptions<XConnectOptions>>().Value,
            sp.GetService<TimeProvider>()));

        return services.AddHttpClient<IXConnectClient, XConnectClient>((http, sp) =>
            {
                // Streaming connections are long-lived, so the pipeline applies per-request timeouts itself.
                http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
                return new XConnectClient(
                    http,
                    sp.GetRequiredService<IOptions<XConnectOptions>>().Value,
                    sp.GetRequiredService<XConnectShared>(),
                    sp.GetService<TimeProvider>(),
                    sp.GetService<ILogger<XConnectClient>>());
            });
    }

    /// <summary>Registers the XConnect 2.0 <see cref="IXClient"/> for the single-call <c>CreatePostAsync</c> scenario.</summary>
    public static IHttpClientBuilder AddXClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddHttpClient<IXClient, XClient>((http, sp) => new XClient(http, sp.GetService<TimeProvider>()));
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }

    private sealed class XConnectOptionsValidator : IValidateOptions<XConnectOptions>
    {
        public ValidateOptionsResult Validate(string? name, XConnectOptions options) =>
            options.Validate() is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
    }
}
