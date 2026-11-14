using System.Net;
using System.Text.Json;
using RoyalTerminal.Multiplexing.Cli;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class MultiplexingCliTests
{
    [Fact]
    public async Task HelpDoesNotRequireCredentialsOrNetwork()
    {
        using HttpClient http = new(new Handler(_ => throw new InvalidOperationException()));
        using StringWriter output = new();
        using StringWriter error = new();
        int exit = await MultiplexingCli.RunAsync(["--help"], null, null, TextReader.Null, output, error, http, default);
        Assert.Equal(0, exit);
        Assert.Contains("api call", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Theory]
    [InlineData("sessions", null)]
    [InlineData("unsupported", "secret")]
    public async Task InvalidUsageIsNonzeroWithoutNetwork(string command, string? token)
    {
        using HttpClient http = new(new Handler(_ => throw new InvalidOperationException()));
        using StringWriter error = new();
        int exit = await MultiplexingCli.RunAsync([command], token, null, TextReader.Null, TextWriter.Null, error, http, default);
        Assert.Equal(2, exit);
        Assert.NotEmpty(error.ToString());
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public async Task RunPreservesArgumentBoundariesAndWorkingDirectory()
    {
        Guid session = Guid.NewGuid();
        string? captured = null;
        using HttpClient http = new(new Handler(async request =>
        {
            Assert.Equal($"/api/sessions/{session:D}/blocks", request.RequestUri!.AbsolutePath);
            captured = await request.Content!.ReadAsStringAsync();
            return new(HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"test\"}") };
        }));
        int exit = await MultiplexingCli.RunAsync(["run", session.ToString(), "--pipe", "--cwd", "/tmp/a b", "--label", "Build",
            "--", "/bin/echo", "one two", "$(literal)"], "secret", null, TextReader.Null, TextWriter.Null, TextWriter.Null, http, default);
        Assert.Equal(0, exit);
        using JsonDocument json = JsonDocument.Parse(captured!);
        Assert.Equal("pipe", json.RootElement.GetProperty("transport").GetString());
        Assert.Equal("/tmp/a b", json.RootElement.GetProperty("workingDirectory").GetString());
        JsonElement command = json.RootElement.GetProperty("command");
        Assert.Equal("/bin/echo", command.GetProperty("fileName").GetString());
        Assert.Equal("one two", command.GetProperty("arguments")[0].GetString());
        Assert.Equal("$(literal)", command.GetProperty("arguments")[1].GetString());
    }

    [Fact]
    public async Task ApiCallAcceptsBoundedJsonFromStdin()
    {
        using HttpClient http = new(new Handler(async request =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("session.new", body.RootElement.GetProperty("method").GetString());
            Assert.Equal("Example", body.RootElement.GetProperty("arguments").GetProperty("label").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }));
        using StringReader input = new("{\"label\":\"Example\"}");
        int exit = await MultiplexingCli.RunAsync(["api", "call", "session.new", "-"], "secret", null,
            input, TextWriter.Null, TextWriter.Null, http, default);
        Assert.Equal(0, exit);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("invalid json")]
    public async Task ApiCallRejectsInvalidArgumentsBeforeNetwork(string json)
    {
        using HttpClient http = new(new Handler(_ => throw new InvalidOperationException()));
        int exit = await MultiplexingCli.RunAsync(["api", "call", "session.new", json], "secret", null,
            TextReader.Null, TextWriter.Null, TextWriter.Null, http, default);
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task DescribeUsesServerCatalogInsteadOfStaticMethodAssumptions()
    {
        using HttpClient http = new(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"methods\":[{\"name\":\"session.new\",\"description\":\"Creates a session\"}]}") })));
        using StringWriter output = new();
        int exit = await MultiplexingCli.RunAsync(["api", "describe", "session.new"], "secret", null,
            TextReader.Null, output, TextWriter.Null, http, default);
        Assert.Equal(0, exit);
        using JsonDocument result = JsonDocument.Parse(output.ToString());
        Assert.Equal("session.new", result.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task SendUsesBase64AndNeverInterpretsShellSyntax()
    {
        Guid session = Guid.NewGuid();
        Guid block = Guid.NewGuid();
        using HttpClient http = new(new Handler(async request =>
        {
            Assert.Equal($"/api/sessions/{session:D}/blocks/{block:D}/input", request.RequestUri!.AbsolutePath);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("JChsaXRlcmFsKQo=", body.RootElement.GetProperty("data").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }));
        int exit = await MultiplexingCli.RunAsync(["send", session.ToString(), block.ToString(), "$(literal)\n"], "secret", null,
            TextReader.Null, TextWriter.Null, TextWriter.Null, http, default);
        Assert.Equal(0, exit);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }
}
