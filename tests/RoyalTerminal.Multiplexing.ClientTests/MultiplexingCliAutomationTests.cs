using System.Net;
using System.Text.Json;
using RoyalTerminal.Multiplexing.Cli;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class MultiplexingCliAutomationTests
{
    private static readonly Guid Session = new("a0000000-0000-0000-0000-00000000abcd");
    private static readonly Guid Block = new("b0000000-0000-0000-0000-000000000002");
    private static readonly Guid Window = new("c0000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task ConfigurationShowReturnsEffectiveConfigAndDiagnostics()
    {
        using HttpClient http = new(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/config", request.RequestUri!.AbsolutePath);
            return Result("""{"configuration":{"version":1,"actions":[]},"diagnostics":[]}""");
        }));
        using StringWriter output = new();
        int exit = await Run(["config", "show"], http, output: output);
        Assert.Equal(0, exit);
        using JsonDocument result = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, result.RootElement.GetProperty("configuration").GetProperty("version").GetInt32());
        Assert.Empty(result.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigurationLoadsFromFileOrStdinWithoutInvokingActions(bool useFile)
    {
        const string configuration = """{"version":1,"actions":[],"bindings":[],"modes":[]}""";
        string? path = useFile ? Path.GetTempFileName() : null;
        int requests = 0;
        try
        {
            if (path is not null) await File.WriteAllTextAsync(path, configuration);
            using StringReader input = new(configuration);
            using HttpClient http = new(new Handler(async request =>
            {
                requests++;
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal("/api/config", request.RequestUri!.AbsolutePath);
                using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Empty(body.RootElement.GetProperty("actions").EnumerateArray());
                return await Result("""{"configuration":{"version":1,"actions":[]},"diagnostics":[]}""");
            }));
            Assert.Equal(0, await Run(["config", "load", path ?? "-"], http, input));
            Assert.Equal(1, requests);
        }
        finally { if (path is not null) File.Delete(path); }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("invalid JSON")]
    public async Task InvalidConfigurationIsRejectedBeforeNetwork(string content)
    {
        using HttpClient http = new(new Handler(_ => throw new InvalidOperationException("No network expected.")));
        using StringReader input = new(content);
        Assert.Equal(2, await Run(["config", "load", "-"], http, input));
    }

    [Fact]
    public async Task ConfigurationBudgetCountsUtf8BytesRatherThanOnlyCharacters()
    {
        using HttpClient http = new(new Handler(_ => throw new InvalidOperationException("No network expected.")));
        using StringReader input = new("{\"value\":\"" + new string('界', 350_000) + "\"}");
        using StringWriter error = new();
        Assert.Equal(2, await Run(["config", "load", "-"], http, input, error: error));
        Assert.Contains("one MiB", error.ToString());
    }

    [Fact]
    public async Task ActionsListsIdsFromResolvedConfiguration()
    {
        using HttpClient http = new(new Handler(request =>
        {
            Assert.Equal("/api/config", request.RequestUri!.AbsolutePath);
            return Result("""{"configuration":{"actions":[{"id":"capture_focused","steps":[]},{"id":"build","steps":[]}]},"diagnostics":[]}""");
        }));
        using StringWriter output = new();
        Assert.Equal(0, await Run(["actions"], http, output: output));
        using JsonDocument result = JsonDocument.Parse(output.ToString());
        Assert.Equal(["capture_focused", "build"], result.RootElement.GetProperty("actions").EnumerateArray().Select(x => x.GetString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActionUsesEnvironmentContextUnlessExplicitSessionReplacesIt(bool explicitSession)
    {
        Guid other = Guid.NewGuid();
        using HttpClient http = new(new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/actions/capture_focused/invoke", request.RequestUri!.AbsolutePath);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(explicitSession ? other : Session, body.RootElement.GetProperty("sessionId").GetGuid());
            if (explicitSession)
            {
                Assert.False(body.RootElement.TryGetProperty("blockId", out _));
                Assert.False(body.RootElement.TryGetProperty("windowId", out _));
            }
            else
            {
                Assert.Equal(Block, body.RootElement.GetProperty("blockId").GetGuid());
                Assert.Equal(Window, body.RootElement.GetProperty("windowId").GetGuid());
            }
            return await Result("{\"actionId\":\"capture_focused\",\"steps\":[]}");
        }));
        string[] args = explicitSession ? ["do", "capture_focused", other.ToString()] : ["do", "capture_focused"];
        Assert.Equal(0, await Run(args, http, context: new(Session.ToString(), Block.ToString(), Window.ToString())));
    }

    [Fact]
    public async Task ActionResolvesSessionBlockAndWindowWithOneSharedDescriptor()
    {
        List<string> paths = [];
        using HttpClient http = new(new Handler(async request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            if (path == "/api/sessions")
                return await Result($$"""{"sessions":[{"id":"{{Session:D}}","label":"Work"}]}""");
            if (path == $"/api/sessions/{Session:D}")
                return await Result($$"""{"blocks":[{"id":"{{Block:D}}","label":"Build"}],"windows":[{"id":"{{Window:D}}","label":"Main"}]}""");
            Assert.Equal("/api/actions/capture_focused/invoke", path);
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(Session, body.RootElement.GetProperty("sessionId").GetGuid());
            Assert.Equal(Block, body.RootElement.GetProperty("blockId").GetGuid());
            Assert.Equal(Window, body.RootElement.GetProperty("windowId").GetGuid());
            return await Result("{}");
        }));
        Assert.Equal(0, await Run(["do", "capture_focused", "work", "BUILD", "main"], http));
        Assert.Equal(["/api/sessions", $"/api/sessions/{Session:D}", "/api/actions/capture_focused/invoke"], paths);
    }

    [Fact]
    public async Task ActionWithoutContextSendsAnEmptyObject()
    {
        using HttpClient http = new(new Handler(async request =>
        {
            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Empty(body.RootElement.EnumerateObject());
            return await Result("{}");
        }));
        Assert.Equal(0, await Run(["do", "global_action"], http));
    }

    [Theory]
    [InlineData("../bad")]
    [InlineData("a/b")]
    [InlineData("a?query=1")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task InvalidActionIdsCannotRedirectRequests(string action)
    {
        using HttpClient http = new(new Handler(_ => throw new InvalidOperationException("No network expected.")));
        Assert.Equal(2, await Run(["do", action], http));
    }

    [Fact]
    public async Task BlockContextRequiresSessionBeforeNetwork()
    {
        using HttpClient http = new(new Handler(_ => throw new InvalidOperationException("No network expected.")));
        Assert.Equal(2, await Run(["do", "capture_focused"], http, context: new(Block: Block.ToString())));
    }

    [Theory]
    [InlineData("WORK")]
    [InlineData("1")]
    [InlineData("abcd")]
    public async Task SessionSelectorsResolveLabelIndexAndUniqueSuffix(string selector)
    {
        int requests = 0;
        using HttpClient http = new(new Handler(request =>
        {
            requests++;
            if (request.RequestUri!.AbsolutePath == "/api/sessions")
                return Result($$"""{"sessions":[{"id":"{{Session:D}}","label":"Work"}]}""");
            Assert.Equal($"/api/sessions/{Session:D}", request.RequestUri.AbsolutePath);
            return Result("{}");
        }));
        Assert.Equal(0, await Run(["show", selector], http));
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task NumericLabelsTakePrecedenceOverListingIndices()
    {
        Guid second = Guid.NewGuid();
        using HttpClient http = new(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/sessions")
                return Result($$"""{"sessions":[{"id":"{{Session:D}}","label":"Work"},{"id":"{{second:D}}","label":"1"}]}""");
            Assert.Equal($"/api/sessions/{second:D}", request.RequestUri.AbsolutePath);
            return Result("{}");
        }));
        Assert.Equal(0, await Run(["show", "1"], http));
    }

    [Fact]
    public async Task AmbiguousLabelCannotDeleteAnyTarget()
    {
        int requests = 0;
        using HttpClient http = new(new Handler(request =>
        {
            requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/sessions", request.RequestUri!.AbsolutePath);
            return Result($$"""{"sessions":[{"id":"{{Session:D}}","label":"Work"},{"id":"{{Guid.NewGuid():D}}","label":"WORK"}]}""");
        }));
        using StringWriter error = new();
        Assert.Equal(2, await Run(["close", "work"], http, error: error));
        Assert.Contains("AmbiguousTarget", error.ToString());
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData("{\"sessions\":null}")]
    [InlineData("{\"sessions\":[{\"id\":\"../escape\",\"label\":\"Work\"}]}")]
    public async Task InvalidHostTargetListingsCannotBecomeMutationPaths(string response)
    {
        int requests = 0;
        using HttpClient http = new(new Handler(request =>
        {
            requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            return Result(response);
        }));
        Assert.Equal(1, await Run(["close", "work"], http));
        Assert.Equal(1, requests);
    }

    private static Task<int> Run(string[] args, HttpClient http, TextReader? input = null,
        TextWriter? output = null, TextWriter? error = null, CliCurrentContext? context = null)
        => MultiplexingCli.RunAsync(args, "secret", null, input ?? TextReader.Null, output ?? TextWriter.Null,
            error ?? TextWriter.Null, http, default, context);

    private static Task<HttpResponseMessage> Result(string json)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handle(request);
    }
}
