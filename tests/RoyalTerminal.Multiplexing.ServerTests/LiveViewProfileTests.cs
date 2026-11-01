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
    [InlineData("native",1,1)] [InlineData("native",2,2)] [InlineData("native",3,3)] [InlineData("native",100,3)]
    [InlineData("browser",50,1)] [InlineData("mobile",3,1)]
    public void NegotiationNeverExceedsTheImplementedOrOfferedVersion(string kind,int offer,int expected)
        =>Assert.Equal(expected,ViewCapabilities.Negotiate(kind,offer));

    [Fact]
    public async Task ScopedOverviewReceivesZoomButCannotDiscoverOrSendItAndWithdrawalRemovesIt()
    {
        const string token="live-view-negotiation-0123456789abcdef";
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));var ct=timeout.Token;
        await using var host=ServerHost.Build(["--Logging:LogLevel:Default=None"],administratorToken:token,listenUrl:"http://127.0.0.1:0");
        await host.StartAsync(ct);
        try
        {
            var broker=host.Services.GetRequiredService<MultiplexerBroker>();var initial=broker.CreateSession("control-room");
            var endpoint=new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            using var http=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false});var admin=new MultiplexingApiClient(http,endpoint,token);
            JsonElement Args(object args)=>JsonSerializer.SerializeToElement(args);
            var share=await admin.CallAsync("share.create",Args(new{sessionId=initial.Id,role="observer"}),ct);
            var observer=new MultiplexingApiClient(http,endpoint,share.GetProperty("token").GetString()!);
            var registration=new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
            var effect=new TaskCompletionSource<MultiplexingViewAction>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var consent=CancellationTokenSource.CreateLinkedTokenSource(ct);
            var loop=MultiplexingViewReceiver.RunOverviewAsync(observer,initial.Id,"View",(a,_)=>{effect.TrySetResult(a);return ValueTask.FromResult(true);},id=>registration.TrySetResult(id),consent.Token);
            try
            {
                var id=await registration.Task.WaitAsync(ct);var target=Args(new{sessionId=initial.Id,clientId=id});
                var catalog=await admin.CallAsync("client.actions",target,ct);Assert.Equal(3,catalog.GetProperty("navigationVersion").GetInt32());
                Assert.Contains(catalog.GetProperty("forms").EnumerateArray(),f=>f.GetProperty("name").GetString()=="pane.zoom");
                var batch=Args(new{sessionId=initial.Id,clientId=id,actions=new[]{new{name="pane.zoom"}}});
                Assert.Equal(HttpStatusCode.Forbidden,(await Assert.ThrowsAsync<MultiplexingApiException>(()=>observer.CallAsync("client.queue",batch,ct).AsTask())).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden,(await Assert.ThrowsAsync<MultiplexingApiException>(()=>observer.CallAsync("client.actions",target,ct).AsTask())).StatusCode);
                var receipt=await admin.CallAsync("client.queue",batch,ct);Assert.Equal(1L,receipt.GetProperty("sequence").GetInt64());
                Assert.Equal("pane.zoom",(await effect.Task.WaitAsync(ct)).Name);Assert.Equal(initial.Revision,broker.GetSession(initial.Id).Revision);
            }
            finally {consent.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>loop);}
            var list=await admin.CallAsync("client.views",Args(new{sessionId=initial.Id}),ct);Assert.Empty(list.EnumerateArray());
        }
        finally {await host.StopAsync();}
    }
}
