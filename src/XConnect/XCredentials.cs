namespace XConnect;

/// <summary>
/// OAuth 1.0a user-context credentials issued in the X Developer Portal.
/// </summary>
/// <param name="ConsumerKey">The app's API key.</param>
/// <param name="ConsumerSecret">The app's API key secret.</param>
/// <param name="AccessToken">The user's access token.</param>
/// <param name="AccessTokenSecret">The user's access token secret.</param>
public sealed record XCredentials(
    string ConsumerKey,
    string ConsumerSecret,
    string AccessToken,
    string AccessTokenSecret)
{
    /// <summary>Throws if any credential value is missing.</summary>
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ConsumerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConsumerSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(AccessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(AccessTokenSecret);
    }

    /// <summary>Masks secrets so credentials never leak into logs.</summary>
    public override string ToString() => $"{nameof(XCredentials)} {{ ConsumerKey = {ConsumerKey}, *** }}";
}
