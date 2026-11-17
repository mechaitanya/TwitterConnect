using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XConnect;

/// <summary>
/// Default <see cref="IXClient"/> implementation that calls <c>POST /2/tweets</c> on the X API.
/// </summary>
/// <param name="httpClient">
/// The HTTP client to use. Supply a long-lived instance (or one from <c>IHttpClientFactory</c>)
/// rather than creating one per call.
/// </param>
/// <param name="timeProvider">Clock used for OAuth timestamps; defaults to the system clock.</param>
public sealed class XClient(HttpClient httpClient, TimeProvider? timeProvider = null) : IXClient
{
    /// <summary>The X API v2 endpoint for creating Posts.</summary>
    public static readonly Uri CreatePostUri = new("https://api.x.com/2/tweets");

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Creates a client with its own <see cref="HttpClient"/>.</summary>
    public XClient() : this(new HttpClient())
    {
    }

    /// <inheritdoc />
    public async Task<XPostResult> CreatePostAsync(
        XCredentials credentials,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        credentials.Validate();

        using var request = new HttpRequestMessage(HttpMethod.Post, CreatePostUri)
        {
            Content = JsonContent.Create(new CreatePostRequest(text), XJsonContext.Default.CreatePostRequest),
        };

        request.Headers.TryAddWithoutValidation("Authorization", OAuth1Signer.CreateAuthorizationHeader(
            HttpMethod.Post,
            CreatePostUri,
            credentials,
            nonce: Guid.NewGuid().ToString("N"),
            timestamp: _timeProvider.GetUtcNow().ToUnixTimeSeconds()));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var rateLimit = ParseRateLimit(response);

        if (response.IsSuccessStatusCode)
        {
            var created = TryDeserialize(body, XJsonContext.Default.CreatePostResponse)?.Data;
            return new XPostResult(response.StatusCode, created?.Id, created?.Text, null, rateLimit);
        }

        return new XPostResult(response.StatusCode, null, null, ExtractError(body), rateLimit);
    }

    private static XRateLimit? ParseRateLimit(HttpResponseMessage response)
    {
        if (TryGetHeader(response, "x-rate-limit-limit", out var limit) &&
            TryGetHeader(response, "x-rate-limit-remaining", out var remaining) &&
            TryGetHeader(response, "x-rate-limit-reset", out var reset))
        {
            return new XRateLimit(
                int.Parse(limit, CultureInfo.InvariantCulture),
                int.Parse(remaining, CultureInfo.InvariantCulture),
                DateTimeOffset.FromUnixTimeSeconds(long.Parse(reset, CultureInfo.InvariantCulture)));
        }

        return null;
    }

    private static bool TryGetHeader(HttpResponseMessage response, string name, out string value)
    {
        value = response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() ?? string.Empty : string.Empty;
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static string? ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var problem = TryDeserialize(body, XJsonContext.Default.ProblemResponse);
        return problem?.Detail ?? problem?.Title ?? problem?.Message ?? body;
    }

    private static T? TryDeserialize<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed record CreatePostRequest(string Text);

    internal sealed record CreatePostResponse(PostData? Data);

    internal sealed record PostData(string Id, string Text);

    internal sealed record ProblemResponse(string? Title, string? Detail, string? Message);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(XClient.CreatePostRequest))]
[JsonSerializable(typeof(XClient.CreatePostResponse))]
[JsonSerializable(typeof(XClient.ProblemResponse))]
internal sealed partial class XJsonContext : JsonSerializerContext;
