using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using RoyalTerminal.Multiplexing.Server;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class ServerIntegrationTests
{
    [Fact]
    public async Task HttpAuthorizationOriginAndStrictJsonValidationAreEnforced()
    {
        await using Host host = await Host.StartAsync();
        using HttpClient anonymous = new() { BaseAddress = host.Address };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/sessions")).StatusCode);
        using HttpRequestMessage foreign = new(HttpMethod.Get, "api/sessions");
        foreign.Headers.Add("Origin", "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(foreign)).StatusCode);
        using StringContent bad = new("{\"label\":\"work\",\"unknown\":1}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsync("api/sessions", bad)).StatusCode);
        using HttpResponseMessage created = await host.Client.PostAsJsonAsync("api/sessions", new NewSession("work"), ServerJsonContext.Default.NewSession);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal("work", document.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public async Task ScopedShareListsOneSessionAndCannotCreateOrEscalateControl()
    {
        await using Host host = await Host.StartAsync();
        Guid session = await host.CreateSessionAsync("shared");
        _ = await host.CreateSessionAsync("private");
        JsonElement share = await host.CallAsync("share.create", $$"""{"sessionId":"{{session}}","role":"observer"}""");
        using HttpClient observer = new() { BaseAddress = host.Address };
        observer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", share.GetProperty("token").GetString());
        using JsonDocument visible = JsonDocument.Parse(await observer.GetStringAsync("api/sessions"));
        Assert.Single(visible.RootElement.GetProperty("sessions").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await observer.PostAsJsonAsync("api/sessions", new NewSession("denied"), ServerJsonContext.Default.NewSession)).StatusCode);
        Guid block = await host.CreateEchoBlockAsync(session);
        await using MultiplexingTerminalTransport transport = new();
        await Assert.ThrowsAnyAsync<Exception>(() => transport.StartAsync(new(host.Address, share.GetProperty("token").GetString()!, session, block, new(80,24,0,0))).AsTask());
    }

    [Fact]
    public async Task LiveNativeClientReplaysGeometryAndContinuesAfterDetach()
    {
        if (OperatingSystem.IsWindows()) return; // Real /bin/sh transport exercised on Linux/macOS CI.
        await using Host host = await Host.StartAsync();
        Guid session = await host.CreateSessionAsync("durable");
        Guid block = await host.CreateEchoBlockAsync(session);
        TaskCompletionSource firstOutput = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StringBuilder received = new();
        bool geometrySeen = false;
        await using (MultiplexingTerminalTransport first = new())
        {
            first.AuthoritativeDimensionsChanged += _ => geometrySeen = true;
            first.DataReceived += (bytes, length) =>
            {
                Assert.True(geometrySeen);
                lock (received) { received.Append(Encoding.UTF8.GetString(bytes,0,length)); if(received.ToString().Contains("first-marker",StringComparison.Ordinal))firstOutput.TrySetResult(); }
            };
            await first.StartAsync(new(host.Address, Host.Token, session, block, new(80,24,0,0)));
            first.SendInput("first-marker\n"u8);
            await first.FlushAsync();
            await firstOutput.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await first.StopAsync();
        }
        TaskCompletionSource secondOutput = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using MultiplexingTerminalTransport second = new();
        StringBuilder replay = new();
        second.DataReceived += (bytes,length) => { lock(replay) { replay.Append(Encoding.UTF8.GetString(bytes,0,length));if(replay.ToString().Contains("second-marker",StringComparison.Ordinal))secondOutput.TrySetResult(); } };
        await second.StartAsync(new(host.Address, Host.Token, session, block, new(80,24,0,0)));
        second.SendInput("second-marker\n"u8);
        await second.FlushAsync();
        await secondOutput.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("first-marker", replay.ToString());
        Assert.Contains("second-marker", replay.ToString());
        using JsonDocument capture = JsonDocument.Parse(await host.Client.GetStringAsync($"api/sessions/{session}/blocks/{block}/capture"));
        Assert.Contains("second-marker",capture.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task DiscoveryAndDispatchRejectUnknownOrMisspelledMethodsWithoutMutation()
    {
        await using Host host = await Host.StartAsync();
        using JsonDocument methods=JsonDocument.Parse(await host.Client.GetStringAsync("api/methods"));
        Assert.Contains(methods.RootElement.GetProperty("methods").EnumerateArray(), x=>x.GetProperty("name").GetString()=="terminal.format");
        Guid session=await host.CreateSessionAsync("before");
        using StringContent body=new($$"""{"method":"session.set_label","arguments":{"sessionId":"{{session}}","lable":"after"}}""",Encoding.UTF8,"application/json");
        Assert.Equal(HttpStatusCode.BadRequest,(await host.Client.PostAsync("api/call",body)).StatusCode);
        using JsonDocument view=JsonDocument.Parse(await host.Client.GetStringAsync($"api/sessions/{session}"));
        Assert.Equal("before",view.RootElement.GetProperty("label").GetString());
    }

    private sealed class Host : IAsyncDisposable
    {
        public const string Token="local-integration-test-token-01234567890123456789";
        private readonly WebApplication _app;
        public Uri Address {get;}
        public HttpClient Client {get;}
        private Host(WebApplication app,Uri address)
        {
            _app=app;Address=address;Client=new(){BaseAddress=address};
            Client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",Token);
        }
        public static async Task<Host> StartAsync()
        {
            WebApplication app=ServerHost.Build(["--Logging:LogLevel:Default=None"],administratorToken:Token,listenUrl:"http://127.0.0.1:0");
            await app.StartAsync();
            string address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app,new Uri(address));
        }
        public async Task<Guid> CreateSessionAsync(string label)
        {
            using HttpResponseMessage result=await Client.PostAsJsonAsync("api/sessions",new NewSession(label),ServerJsonContext.Default.NewSession);
            result.EnsureSuccessStatusCode();using JsonDocument doc=JsonDocument.Parse(await result.Content.ReadAsStringAsync());return doc.RootElement.GetProperty("id").GetGuid();
        }
        public async Task<Guid> CreateEchoBlockAsync(Guid session)
        {
            TerminalCommandSpec command=OperatingSystem.IsWindows()?new("cmd.exe",["/c","more"]):new("/bin/sh",["-c","while IFS= read -r line; do printf '%s\\n' \"$line\"; done"]);
            NewBlock request=new("Echo","pipe",command,Dimensions:new(80,24,0,0));
            using HttpResponseMessage result=await Client.PostAsJsonAsync($"api/sessions/{session}/blocks",request,ServerJsonContext.Default.NewBlock);
            result.EnsureSuccessStatusCode();using JsonDocument doc=JsonDocument.Parse(await result.Content.ReadAsStringAsync());return doc.RootElement.GetProperty("id").GetGuid();
        }
        public async Task<JsonElement> CallAsync(string method,string arguments)
        {
            using JsonDocument args=JsonDocument.Parse(arguments);
            using HttpResponseMessage result=await Client.PostAsJsonAsync("api/call",new ApiCall(method,args.RootElement),ServerJsonContext.Default.ApiCall);
            result.EnsureSuccessStatusCode();using JsonDocument doc=JsonDocument.Parse(await result.Content.ReadAsStringAsync());return doc.RootElement.Clone();
        }
        public async ValueTask DisposeAsync(){Client.Dispose();await _app.StopAsync();await _app.DisposeAsync();}
    }
}
