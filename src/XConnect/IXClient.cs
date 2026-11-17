namespace XConnect;

/// <summary>Creates Posts on X via the X API v2.</summary>
public interface IXClient
{
    /// <summary>Publishes a Post for the user that owns <paramref name="credentials"/>.</summary>
    /// <param name="credentials">OAuth 1.0a user-context credentials.</param>
    /// <param name="text">The Post text (up to 280 characters unless the account has X Premium).</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result, including the HTTP status, Post ID and rate limit details.</returns>
    /// <exception cref="ArgumentException">A credential value or the text is empty.</exception>
    /// <exception cref="HttpRequestException">The request could not be completed.</exception>
    Task<XPostResult> CreatePostAsync(XCredentials credentials, string text, CancellationToken cancellationToken = default);
}
