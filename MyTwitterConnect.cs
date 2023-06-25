using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace TwitterConnect
{
    public class MyTwitterConnect
    {
        private const string URL = "https://api.twitter.com/2/tweets";

        public async Task<HttpResponseMessage?> AuthenticateUserAndSendTweet(Credentials creds, string message)
        {
            try
            {
                var httpClient = new HttpClient();
                string json = JsonConvert.SerializeObject(new { text = message });

                var tweetContent = new StringContent(json, Encoding.UTF8, "application/json");
                var oauthNonce = Guid.NewGuid().ToString("N");
                var oauthTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

                var oauthSignature = GenerateOAuthSignature(creds.ConsumerKey, creds.ConsumerSecret, creds.AccessToken, creds.AccessTokenSecret, oauthNonce, oauthTimestamp, tweetContent);

                var authorizationHeader = GenerateAuthorizationHeader(creds.ConsumerKey, creds.AccessToken, oauthNonce, oauthTimestamp, oauthSignature);

                httpClient.DefaultRequestHeaders.Add("Authorization", authorizationHeader);

                var response = await httpClient.PostAsync(URL, tweetContent);

                return response;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GenerateOAuthSignature(string apiKey, string apiSecretKey, string accessToken, string accessTokenSecret, string oauthNonce, string oauthTimestamp, HttpContent tweetContent)
        {
            var signatureBaseString = string.Format(
                "POST&{0}&{1}",
                Uri.EscapeDataString(URL),
                Uri.EscapeDataString(
                    $"oauth_consumer_key={apiKey}&" +
                    $"oauth_nonce={oauthNonce}&" +
                    $"oauth_signature_method=HMAC-SHA1&" +
                    $"oauth_timestamp={oauthTimestamp}&" +
                    $"oauth_token={accessToken}&" +
                    "oauth_version=1.0"
                )
            );

            var signingKey = $"{Uri.EscapeDataString(apiSecretKey)}&{Uri.EscapeDataString(accessTokenSecret)}";
            var hmacsha1 = new HMACSHA1(Encoding.UTF8.GetBytes(signingKey));

            var oauthSignatureBytes = hmacsha1.ComputeHash(Encoding.UTF8.GetBytes(signatureBaseString));

            return Convert.ToBase64String(oauthSignatureBytes);
        }

        private static string GenerateAuthorizationHeader(string apiKey, string accessToken, string oauthNonce, string oauthTimestamp, string oauthSignature)
        {
            return $"OAuth " +
                $"oauth_consumer_key=\"{Uri.EscapeDataString(apiKey)}\", " +
                $"oauth_nonce=\"{Uri.EscapeDataString(oauthNonce)}\", " +
                $"oauth_signature=\"{Uri.EscapeDataString(oauthSignature)}\", " +
                $"oauth_signature_method=\"HMAC-SHA1\", " +
                $"oauth_timestamp=\"{Uri.EscapeDataString(oauthTimestamp)}\", " +
                $"oauth_token=\"{Uri.EscapeDataString(accessToken)}\", " +
                "oauth_version=\"1.0\"";
        }
    }

    public class Credentials
    {
        public string ConsumerKey { get; set; }
        public string ConsumerSecret { get; set; }
        public string AccessToken { get; set; }
        public string AccessTokenSecret { get; set; }
    }
}