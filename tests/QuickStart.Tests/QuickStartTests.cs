using System.Net;
using System.Text;
using AwesomeAssertions;
using QuickStart;

namespace QuickStart.Tests;

// Fully stubbed: no network. These cover the sample's own logic only. They say nothing about the real X API or about the Playground.
public sealed class QuickStartTests
{
    private const string RealToken = "REAL-TOKEN-VALUE";

    private sealed class Recorder(string body = """{"data":{"id":"0","name":"Playground User","username":"playground_user"}}""", HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static Func<string, string?> Env(params (string Key, string Value)[] values) =>
        key => values.Where(v => v.Key == key).Select(v => v.Value).FirstOrDefault();

    private static readonly (string, string) Sim = ("XCONNECT_SIMULATOR", "1");

    private static string? NoFile(string _) => null;

    private static async Task<(int Code, string Output, Recorder Handler)> Run(string[] args, Func<string, string?> env, Recorder? handler = null)
    {
        handler ??= new Recorder();
        var output = new StringWriter();
        var code = await QuickStartApp.RunAsync(args, QuickStartConfig.Load(env, NoFile), handler, output);
        return (code, output.ToString(), handler);
    }

    [Fact]
    public void Unset_BaseAddress_KeepsTheRealApiDefault()
    {
        QuickStartConfig.ParseBaseAddress(null).Should().BeNull();
        QuickStartConfig.ParseBaseAddress("  ").Should().BeNull();
        var config = QuickStartConfig.Load(Env(("XCONNECT_BEARER_TOKEN", RealToken)), NoFile);
        config.IsSimulator.Should().BeFalse();
        QuickStartApp.BuildOptions(config).BaseAddress.Should().Be(new Uri("https://api.x.com/"));
    }

    [Fact]
    public async Task Custom_BaseAddress_IsUsedForRequests()
    {
        var (code, _, handler) = await Run(["--id", "0"], Env(("XCONNECT_BASE_ADDRESS", "http://127.0.0.1:18080"), Sim));
        code.Should().Be(0);
        handler.Requests.Should().ContainSingle().Which.RequestUri.Should().Be(new Uri("http://127.0.0.1:18080/2/users/0"));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://127.0.0.1:21")]
    [InlineData("127.0.0.1:18080")]
    public async Task Invalid_BaseAddress_FailsClearly(string value)
    {
        var act = () => QuickStartConfig.ParseBaseAddress(value);
        act.Should().Throw<QuickStartConfigException>().WithMessage("*XCONNECT_BASE_ADDRESS*absolute http or https*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LookupById_CallsTheIdEndpointAndPrintsTheUser()
    {
        var (code, output, handler) = await Run(["--id", "0"], Env(("XCONNECT_BASE_ADDRESS", "http://127.0.0.1:18080"), Sim));
        code.Should().Be(0);
        handler.Requests.Single().Method.Should().Be(HttpMethod.Get);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/2/users/0");
        output.Should().Contain("Playground User (@playground_user), id 0");
    }

    [Fact]
    public async Task LookupByUsername_StillWorks_AndAcceptsALeadingAt()
    {
        var (code, output, handler) = await Run(["@jack"], Env(("XCONNECT_BEARER_TOKEN", RealToken)));
        code.Should().Be(0);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/2/users/by/username/jack");
        handler.Requests.Single().RequestUri!.Host.Should().Be("api.x.com");
        output.Should().Contain("id 0");
    }

    [Fact]
    public async Task RealMode_NeverSendsTheSimulatorHeader_AndUsesTheRealToken()
    {
        var (_, output, handler) = await Run(["--id", "0"], Env(("XCONNECT_BEARER_TOKEN", RealToken)));
        var request = handler.Requests.Single();
        request.Headers.Contains("X-Auth-Method").Should().BeFalse();
        request.Headers.Authorization!.ToString().Should().Be($"Bearer {RealToken}");
        output.Should().NotContain("SIMULATOR").And.NotContain(RealToken);
    }

    [Fact]
    public async Task RealMode_RejectsMe_WithoutSendingAnything()
    {
        var (code, _, handler) = await Run(["--me"], Env(("XCONNECT_BEARER_TOKEN", RealToken)));
        code.Should().Be(1);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Simulator_UsesThePlaceholderToken_NotTheRealOne_AndNoHeaderForIdLookups()
    {
        var env = Env(("XCONNECT_BASE_ADDRESS", "http://127.0.0.1:18080"), Sim, ("XCONNECT_BEARER_TOKEN", RealToken));
        var (_, output, handler) = await Run(["--id", "0"], env);
        var request = handler.Requests.Single();
        request.Headers.Authorization!.ToString().Should().Be("Bearer test_token");
        request.Headers.Contains("X-Auth-Method").Should().BeFalse();
        output.Should().Contain("SIMULATOR").And.Contain("not real X API");
        output.Should().NotContain("test_token").And.NotContain(RealToken);
    }

    [Fact]
    public async Task Simulator_Me_AddsTheHeaderOnlyOnThatRequest()
    {
        var (code, _, handler) = await Run(["--me"], Env(("XCONNECT_BASE_ADDRESS", "http://127.0.0.1:18080"), Sim));
        code.Should().Be(0);
        var request = handler.Requests.Single();
        request.RequestUri!.AbsolutePath.Should().Be("/2/users/me");
        request.Headers.GetValues("X-Auth-Method").Should().ContainSingle().Which.Should().Be("oauth2");
        request.Headers.Authorization!.ToString().Should().Be("Bearer test_token");
    }

    [Fact]
    public async Task ExplicitRealHost_IsTreatedAsTheRealApi()
    {
        var (_, _, handler) = await Run(["--id", "0"], Env(("XCONNECT_BASE_ADDRESS", "https://api.x.com"), ("XCONNECT_BEARER_TOKEN", RealToken)));
        handler.Requests.Single().Headers.Authorization!.ToString().Should().Be($"Bearer {RealToken}");
    }

    [Fact]
    public async Task RealMode_Options_HaveNoUserContextStandIn()
    {
        // The simulator authenticator must not exist in real mode: a user-context call has nothing to authenticate with, so the SDK refuses it
        // locally and nothing (with or without the nonstandard header) reaches the network.
        var handler = new Recorder();
        using var http = new HttpClient(handler);
        var client = new XConnect.XConnectClient(http, QuickStartApp.BuildOptions(QuickStartConfig.Load(Env(("XCONNECT_BEARER_TOKEN", RealToken)), NoFile)));

        var act = () => client.Users.GetMeAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownUser_ReportedAs200WithErrorsAndNoData_IsHandledWithoutACrash()
    {
        var handler = new Recorder("""{"errors":[{"detail":"Could not find user with id: [999].","title":"Not Found Error","type":"https://api.twitter.com/2/problems/resource-not-found"}]}""");
        var (code, output, _) = await Run(["--id", "999"], Env(("XCONNECT_BEARER_TOKEN", RealToken)), handler);
        code.Should().Be(2);
        output.Should().Contain("No user found").And.Contain("Could not find user");
    }

    [Fact]
    public void CustomHost_WithoutTheExplicitFlag_IsRefused_NotInferredAsASimulator()
    {
        var act = () => QuickStartConfig.Load(Env(("XCONNECT_BASE_ADDRESS", "https://proxy.example.com"), ("XCONNECT_BEARER_TOKEN", RealToken)), NoFile);
        act.Should().Throw<QuickStartConfigException>().WithMessage("*XCONNECT_SIMULATOR=1*").Which.Message.Should().NotContain(RealToken);
    }

    [Theory]
    [InlineData(null, "1")]
    [InlineData("https://api.x.com", "1")]
    public void SimulatorFlag_WithoutACustomAddress_IsRefused(string? address, string flag)
    {
        var env = address is null ? Env(("XCONNECT_SIMULATOR", flag)) : Env(("XCONNECT_BASE_ADDRESS", address), ("XCONNECT_SIMULATOR", flag));
        var act = () => QuickStartConfig.Load(env, NoFile);
        act.Should().Throw<QuickStartConfigException>().WithMessage("*XCONNECT_SIMULATOR=1 needs*");
    }

    [Fact]
    public void RealHost_OverPlainHttp_IsRefused() =>
        ((Action)(() => QuickStartConfig.Load(Env(("XCONNECT_BASE_ADDRESS", "http://api.x.com"), ("XCONNECT_BEARER_TOKEN", RealToken)), NoFile)))
            .Should().Throw<QuickStartConfigException>().WithMessage("*https*");

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("")]
    public void SimulatorFlag_Off_KeepsRealMode(string value) =>
        QuickStartConfig.Load(Env(("XCONNECT_SIMULATOR", value), ("XCONNECT_BEARER_TOKEN", RealToken)), NoFile).IsSimulator.Should().BeFalse();

    [Fact]
    public void SimulatorFlag_WithAnUnknownValue_IsRefused() =>
        ((Action)(() => QuickStartConfig.Load(Env(("XCONNECT_SIMULATOR", "yes")), NoFile))).Should().Throw<QuickStartConfigException>();

    [Fact]
    public async Task RealMode_NeverSendsAPlaceholderOrPlaygroundToken_OnAnyRequest()
    {
        var env = Env(("XCONNECT_BEARER_TOKEN", RealToken), ("XCONNECT_PLAYGROUND_TOKEN", "PLAYGROUND-ONLY"));
        var (_, output, handler) = await Run(["--id", "0"], env);
        var all = AllHeaders(handler.Requests);
        all.Should().Contain(RealToken).And.NotContain("test_token").And.NotContain("PLAYGROUND-ONLY").And.NotContain("X-Auth-Method");
        output.Should().NotContain("SIMULATOR");
    }

    [Fact]
    public async Task Simulator_NeverSendsTheRealToken_FromEnvironmentOrFile_OnAnyRequest()
    {
        const string fileToken = "FILE-REAL-TOKEN";
        var handler = new Recorder();
        var output = new StringWriter();
        var config = QuickStartConfig.Load(Env(Sim, ("XCONNECT_BASE_ADDRESS", "http://127.0.0.1:18080"), ("XCONNECT_BEARER_TOKEN", RealToken)), k => k == "BearerToken" ? fileToken : null);
        await QuickStartApp.RunAsync(["--id", "0"], config, handler, output);
        await QuickStartApp.RunAsync(["--me"], config, handler, output);

        handler.Requests.Should().HaveCount(2);
        var all = AllHeaders(handler.Requests);
        all.Should().Contain("test_token").And.NotContain(RealToken).And.NotContain(fileToken);
        output.ToString().Should().NotContain(RealToken).And.NotContain(fileToken);
        handler.Requests.Count(r => r.Headers.Contains("X-Auth-Method")).Should().Be(1);
        handler.Requests.Single(r => r.Headers.Contains("X-Auth-Method")).RequestUri!.AbsolutePath.Should().Be("/2/users/me");
    }

    [Fact]
    public async Task Simulator_UsesTheConfiguredPlaygroundToken_WhenSet()
    {
        var env = Env(Sim, ("XCONNECT_BASE_ADDRESS", "http://127.0.0.1:18080"), ("XCONNECT_PLAYGROUND_TOKEN", "my_pg_token"), ("XCONNECT_BEARER_TOKEN", RealToken));
        var (_, _, handler) = await Run(["--id", "0"], env);
        handler.Requests.Single().Headers.Authorization!.ToString().Should().Be("Bearer my_pg_token");
    }

    private static string AllHeaders(IEnumerable<HttpRequestMessage> requests) =>
        string.Join("\n", requests.SelectMany(r => r.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}")));

    [Fact]
    public async Task UnknownUser_ReportedAsHttpError404_IsHandledWithoutACrash()
    {
        var handler = new Recorder("""{"title":"Not Found Error","detail":"Could not find user.","type":"https://api.twitter.com/2/problems/resource-not-found"}""", HttpStatusCode.NotFound);
        var (code, output, _) = await Run(["--id", "999"], Env(("XCONNECT_BEARER_TOKEN", RealToken)), handler);
        code.Should().Be(2);
        output.Should().Contain("No user found").And.Contain("404");
    }

    [Fact]
    public async Task KnownUser_StillPrintsNormallyWithExitCodeZero()
    {
        var (code, output, _) = await Run(["x"], Env(("XCONNECT_BEARER_TOKEN", RealToken)));
        code.Should().Be(0);
        output.Should().Contain("Playground User (@playground_user), id 0").And.NotContain("No user found");
    }

    [Fact]
    public async Task ApiErrors_AreReportedWithAnExitCode()
    {
        var handler = new Recorder("""{"title":"Payment Required","detail":"credits depleted","type":"https://api.x.com/2/problems/credits-depleted"}""", HttpStatusCode.PaymentRequired);
        var (code, output, _) = await Run(["x"], Env(("XCONNECT_BEARER_TOKEN", RealToken)), handler);
        code.Should().Be(2);
        output.Should().Contain("PaymentRequired (HTTP 402)").And.Contain("no credits");
    }

    [Fact]
    public async Task BadArguments_PrintUsage()
    {
        var (code, output, handler) = await Run(["--id"], Env(("XCONNECT_BEARER_TOKEN", RealToken)));
        code.Should().Be(1);
        output.Should().Contain("Usage");
        handler.Requests.Should().BeEmpty();
    }
}
