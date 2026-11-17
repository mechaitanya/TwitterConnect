using AwesomeAssertions;

namespace XConnect.Tests;

public sealed class OAuth1SignerTests
{
    // Worked example from the X API documentation ("Creating a signature").
    private static readonly XCredentials DocsCredentials = new(
        "xvz1evFS4wEEPTGEFPHBog",
        "kAcSOqF21Fu85e7zjz7ZN2U4ZRhfV3WpwPAoE3Z7kBw",
        "370773112-GmHxMAgYyLbNEtIKZeRNFsMKPR9EyMZeS9weJAEb",
        "LswwdoUaIvS8ltyTt5jkRh4J50vUPVVHtR2YPi5kE");

    [Fact]
    public void Sign_MatchesDocumentedExample()
    {
        var oauth = new Dictionary<string, string>
        {
            ["oauth_consumer_key"] = DocsCredentials.ConsumerKey,
            ["oauth_nonce"] = "kYjzVBB8Y0ZFabxSWbWovY3uYSQ2pTgmZeNu2VS4cg",
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = "1318622958",
            ["oauth_token"] = DocsCredentials.AccessToken,
            ["oauth_version"] = "1.0",
        };
        var url = new Uri("https://api.twitter.com/1.1/statuses/update.json" +
            "?include_entities=true&status=Hello%20Ladies%20%2b%20Gentlemen%2c%20a%20signed%20OAuth%20request%21");

        var signature = OAuth1Signer.Sign(HttpMethod.Post, url, oauth, DocsCredentials);

        signature.Should().Be("hCtSmYh+iHYCEqBWrE7C7hYmtUk=");
    }

    [Theory]
    [InlineData("abcXYZ019-._~", "abcXYZ019-._~")]
    [InlineData("a b", "a%20b")]
    [InlineData("100%", "100%25")]
    [InlineData("a+b/c=d&e", "a%2Bb%2Fc%3Dd%26e")]
    [InlineData("é", "%C3%A9")]
    public void Encode_FollowsRfc3986(string input, string expected) =>
        OAuth1Signer.Encode(input).Should().Be(expected);

    [Fact]
    public void CreateAuthorizationHeader_ContainsAllOAuthFieldsAndEncodesSignature()
    {
        var header = OAuth1Signer.CreateAuthorizationHeader(
            HttpMethod.Post, XClient.CreatePostUri, DocsCredentials, "nonce123", 1_700_000_000);

        header.Should().StartWith("OAuth ")
            .And.Contain("oauth_consumer_key=\"xvz1evFS4wEEPTGEFPHBog\"")
            .And.Contain("oauth_nonce=\"nonce123\"")
            .And.Contain("oauth_signature_method=\"HMAC-SHA1\"")
            .And.Contain("oauth_timestamp=\"1700000000\"")
            .And.Contain("oauth_version=\"1.0\"")
            .And.MatchRegex("oauth_signature=\"[A-Za-z0-9%.\\-_~]+\"");
    }
}
