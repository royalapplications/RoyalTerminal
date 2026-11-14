using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using RoyalTerminal.Multiplexing.Server;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;
namespace RoyalTerminal.Multiplexing.ServerTests;
public sealed class LiveViewProfileTests
{
    [Theory]
    [InlineData("native",1,1)][InlineData("native",2,2)][InlineData("native",3,3)][InlineData("native",100,3)]
    [InlineData("browser",50,1)][InlineData("mobile",1,1)][InlineData("mobile",2,2)][InlineData("mobile",100,3)]
    public void NegotiationNeverExceedsTheImplementedOrOfferedVersion(string kind,int offer,int expected)=>Assert.Equal(expected,ViewCapabilities.Negotiate(kind,offer));
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ScopedViewCanReportEffectsButCannotPublishOrDiscoverThem(bool phone)
    {
        const string token="live-view-negotiation-0123456789abcdef";
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));var ct=timeout.Token;
        await using var host=ServerHost.Build(["--Logging:LogLevel:Default=None"],administratorToken:token,listenUrl:"http://127.0.0.1:0");await host.StartAsync(ct);
        try{
            var broker=host.Services.GetRequiredService<MultiplexerBroker>();var initial=broker.CreateSession("control-room");
            var endpoint=new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false});var admin=new MultiplexingApiClient(http,endpoint,token);
            JsonElement Args(object args)=>JsonSerializer.SerializeToElement(args);
            var share=await admin.CallAsync("share.create",Args(new{sessionId=initial.Id,role="observer"}),ct);
            var observer=new MultiplexingApiClient(http,endpoint,share.GetProperty("token").GetString()!);
            var registration=new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);using var consent=CancellationTokenSource.CreateLinkedTokenSource(ct);
            var seen=new List<string>();ValueTask<bool> Apply(MultiplexingViewAction a,CancellationToken _){seen.Add(a.Name);return ValueTask.FromResult(a.Name=="pane.zoom");}
            void Registered(Guid id)=>registration.TrySetResult(id);
            var loop=phone?MultiplexingViewReceiver.RunHandheldAsync(observer,initial.Id,"Phone",Apply,Registered,consent.Token):MultiplexingViewReceiver.RunOverviewAsync(observer,initial.Id,"Desktop",Apply,Registered,consent.Token);
            try{
                var id=await registration.Task.WaitAsync(ct);var target=Args(new{sessionId=initial.Id,clientId=id});
                var catalog=await admin.CallAsync("client.actions",target,ct);Assert.Equal(3,catalog.GetProperty("navigationVersion").GetInt32());
                Assert.Contains(catalog.GetProperty("forms").EnumerateArray(),f=>f.GetProperty("name").GetString()=="pane.zoom");
                var batch=Args(new{sessionId=initial.Id,clientId=id,actions=new[]{new{name="pane.zoom"},new{name="client.tab.next"},new{name="pane.zoom"}}});
                var query=Args(new{sessionId=initial.Id,clientId=id,sequence=1,timeoutMs=5000});
                foreach(var (method,args) in new[]{("client.queue",batch),("client.actions",target),("client.view_result",query)})
                    Assert.Equal(HttpStatusCode.Forbidden,(await Assert.ThrowsAsync<MultiplexingApiException>(()=>observer.CallAsync(method,args,ct).AsTask())).StatusCode);
                var receipt=await admin.CallAsync("client.queue",batch,ct);Assert.Equal(1L,receipt.GetProperty("sequence").GetInt64());
                var outcome=await admin.CallAsync("client.view_result",query,ct);Assert.Equal("declined",outcome.GetProperty("state").GetString());Assert.Equal(1,outcome.GetProperty("applied").GetInt32());Assert.Equal(3,outcome.GetProperty("total").GetInt32());
                Assert.Equal(new[]{"pane.zoom","client.tab.next"},seen);Assert.Equal(initial.Revision,broker.GetSession(initial.Id).Revision);
            }finally{consent.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>loop);}
            Assert.Empty((await admin.CallAsync("client.views",Args(new{sessionId=initial.Id}),ct)).EnumerateArray());
        }finally{await host.StopAsync();}
    }
}
