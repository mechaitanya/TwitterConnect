Twitter Connect is a simple class library that authenticates the credentials and tweets the message to clients(customers) twitter handle.

The Library has a simple async function *AuthenticateUserAndSendTweet(Credentials credentials, string tweetmessage)* that accepts the credentials and message that you wish to tweet. It has a class named *Credentials* that has four properties ConsumerKey,ConsumerSecret,AccessToken,AccessTokenSecret. It is a simple light weight library that performs just what it explains. It authenticates the credentials and sends tweet. It returns *HttpResponseMessage* with the status code.Twitter Connect is a simple class library that authenticates the credentials and tweets the message to clients(customers) twitter handle. *It authenticates twitter v2 API and supports .net 6 and 7*.

Steps to implement:


        MyTwitterConnect connect = new();
        Credentials creds = new()
        {
            ConsumerKey = "consumer_key",
            ConsumerSecret = "consumer_secret",
            AccessToken = "access_token",
            AccessTokenSecret = "access_token_secret"
        };
        await connect.AuthenticateUserAndSendTweet(creds, "Thank you" );

The Library has a simple async function *AuthenticateUserAndSendTweet(Credentials credentials, string tweetmessage)* that accepts the credentials and message that you wish to tweet. It has a class named *Credentials* that has four properties ConsumerKey,ConsumerSecret,AccessToken,AccessTokenSecret. It is a simple light weight library that performs just what it explains. It authenticates the credentials and sends tweet. It retuns HttpResponseMessage with the status code as response.