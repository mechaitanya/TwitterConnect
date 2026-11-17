using AwesomeAssertions;

namespace XConnect.IntegrationTests;

/// <summary>
/// These always run and never touch the network or the real environment: they prove the opt-in gates cannot be opened by accident.
/// </summary>
public sealed class LiveGateTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars) => name => vars.FirstOrDefault(v => v.Key == name).Value;

    [Fact]
    public void Nothing_Runs_WithoutTheMasterSwitch()
    {
        Live.SkipReason(Env(("XCONNECT_LIVE_ALLOW_WRITES", Live.WritePhrase)), false, false, []).Should().Contain("XCONNECT_LIVE=1");
        Live.SkipReason(Env(("XCONNECT_LIVE", "true")), false, false, []).Should().Contain("XCONNECT_LIVE=1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("create-and-delete-a-real-post")]
    [InlineData("CREATE-AND-DELETE-A-REAL-POST ")]
    public void WriteTests_NeedTheExactPhrase(string? value)
    {
        var env = value is null ? Env(("XCONNECT_LIVE", "1")) : Env(("XCONNECT_LIVE", "1"), ("XCONNECT_LIVE_ALLOW_WRITES", value));

        Live.SkipReason(env, true, false, []).Should().Contain("creates real content");
    }

    [Fact]
    public void WriteTests_RunOnlyWithTheExactPhraseAndAllCredentials()
    {
        var env = Env(("XCONNECT_LIVE", "1"), ("XCONNECT_LIVE_ALLOW_WRITES", Live.WritePhrase), ("A", "x"));

        Live.SkipReason(env, true, false, ["A"]).Should().BeNull();
        Live.SkipReason(env, true, false, ["A", "B"]).Should().Contain("B").And.NotContain("A,");
    }

    [Fact]
    public void ReadOnlyTests_DoNotNeedTheWritePhrase()
    {
        Live.SkipReason(Env(("XCONNECT_LIVE", "1")), false, false, []).Should().BeNull();
    }

    [Fact]
    public void TokenRotatingTests_NeedTheirOwnOptIn()
    {
        Live.SkipReason(Env(("XCONNECT_LIVE", "1")), false, true, []).Should().Contain("XCONNECT_LIVE_ALLOW_TOKEN_REFRESH");
        Live.SkipReason(Env(("XCONNECT_LIVE", "1"), ("XCONNECT_LIVE_ALLOW_TOKEN_REFRESH", "1")), false, true, []).Should().BeNull();
    }

    [Fact]
    public void EmptyVariables_CountAsMissing()
    {
        Live.SkipReason(Env(("XCONNECT_LIVE", "1"), ("A", "")), false, false, ["A"]).Should().Contain("A");
    }

    [Fact]
    public void WriteSkipMessage_NeverEchoesCredentialValues()
    {
        var reason = Live.SkipReason(Env(("XCONNECT_LIVE", "1"), ("XCONNECT_CONSUMER_SECRET", "SUPER-SECRET")), true, false, ["XCONNECT_CONSUMER_SECRET"]);

        reason.Should().NotContain("SUPER-SECRET");
    }

    [Fact]
    public async Task PostLedger_RefusesToDeleteAPostThisRunDidNotCreate()
    {
        var handler = new ThrowingHandler();
        using var http = new HttpClient(handler);
        var client = new XConnectClient(http, new XConnectOptions().UseBearerToken("t"));
        var ledger = new Live.PostLedger(client, new NullOutput());

        var act = () => ledger.DeleteAsync("12345");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not created by this test run*");
        handler.Calls.Should().Be(0, "the refusal must happen before any request is sent");
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("No request should be sent.");
        }
    }

    private sealed class NullOutput : Xunit.Abstractions.ITestOutputHelper
    {
        public void WriteLine(string message)
        {
        }

        public void WriteLine(string format, params object[] args)
        {
        }
    }
}
