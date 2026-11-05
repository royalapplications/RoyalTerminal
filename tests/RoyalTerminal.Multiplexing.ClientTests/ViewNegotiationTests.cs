using System.Net;
using System.Text;
using System.Text.Json;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class ViewNegotiationTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "method_not_found", true)]
    [InlineData(HttpStatusCode.NotFound, "view_gone", false)]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", false)]
    [InlineData(HttpStatusCode.BadGateway, "upstream", false)]
    public async Task RetryRegistrationOnlyOnDefinitivelyUnknownMethod(HttpStatusCode status,string code,bool fallback)
    {
        var session=Guid.NewGuid(); var client=Guid.NewGuid(); List<string> methods=[];
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var http=new HttpClient(new Handler((method,_)=>{
            methods.Add(method);
            return method switch {
                "client.open_navigator"=>Json(new{code},status),
                "client.open_view"=>Json(new{view=new{id=client,sessionId=session,kind="native"},readerKey=new string('a',64)}),
                "client.close_view"=>Json(new{ok=true}),
                _=>throw new InvalidOperationException(method),
            };
        }));
        Task Run()=>MultiplexingViewReceiver.RunNavigatorAsync(new(http,new("https://workspace.test/"),"private"),session,"Desktop",(_,_)=>ValueTask.FromResult(true),_=>deadline.Cancel(),deadline.Token);
        if (fallback)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
            Assert.Equal(new[]{"client.open_navigator","client.open_view","client.close_view"},methods);
        }
        else
        {
            var error=await Assert.ThrowsAsync<MultiplexingApiException>(Run); Assert.Equal(status,error.StatusCode);
            Assert.Equal(new[]{"client.open_navigator"},methods);
        }
    }

    [Fact]
    public async Task InvalidSecondActionCannotApplyFirstEvenIfFirstIsValid()
    {
        var s=Guid.NewGuid();var id=Guid.NewGuid();int callbacks=0,closed=0;
        using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var http=new HttpClient(new Handler((method,_)=>{
            if(method=="client.open_navigator")return Json(new{view=new{id,sessionId=s,kind="native",navigationVersion=2},readerKey=new string('a',64)});
            if(method=="client.close_view"){closed++;return Json(new{ok=true});}
            return Json(new{cursor=1,batches=new[]{new{sequence=1,actions=new[]{new{name="pane.focus",direction="left"},new{name="block.input",direction="right"}}}}});
        }));
        await Assert.ThrowsAsync<InvalidDataException>(()=>MultiplexingViewReceiver.RunNavigatorAsync(new(http,new("https://workspace.test/"),"private"),s,"Desk",(_,_)=>{callbacks++;return ValueTask.FromResult(true);},cancellationToken:ct.Token));
        Assert.Equal(0,callbacks);Assert.Equal(1,closed);
    }

    private static HttpResponseMessage Json<T>(T value,HttpStatusCode status=HttpStatusCode.OK) => new(status){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    private sealed class Handler(Func<string,JsonElement,HttpResponseMessage> respond):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            Assert.Equal("private",request.Headers.Authorization?.Parameter);
            using var doc=JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            return respond(doc.RootElement.GetProperty("method").GetString()!,doc.RootElement.GetProperty("arguments"));
        }
    }
}
