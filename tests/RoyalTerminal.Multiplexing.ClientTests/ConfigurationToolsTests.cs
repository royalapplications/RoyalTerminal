using System.Net;
using System.Text.Json;
using RoyalTerminal.Multiplexing.Cli;
using Xunit;
namespace RoyalTerminal.Multiplexing.ClientTests;
public sealed class ConfigurationToolsTests
{
    [Theory][InlineData("check","lua.check")][InlineData("reload","lua.reload")]
    public async Task ReadOnlyCheckNeverUsesTheMutationMethod(string command,string method)
    {
        using var http=new HttpClient(new Handler(method));using var output=new StringWriter();using var error=new StringWriter();
        Assert.Equal(0,await ConfigurationTools.RunAsync(["config",command],null,"secret",http,output,error,default));Assert.Contains("generation",output.ToString());Assert.Empty(error.ToString());
    }
    [Fact]public async Task ModeFilteringPreservesTheManifestGenerationAndNeverRunsAnAction()
    {
        using var http=new HttpClient(new Handler("lua.actions"));using var output=new StringWriter();using var error=new StringWriter();
        Assert.Equal(0,await ConfigurationTools.RunAsync(["keymap","--mode","edit"],null,"secret",http,output,error,default));using var json=JsonDocument.Parse(output.ToString());var binding=Assert.Single(json.RootElement.GetProperty("bindings").EnumerateArray());Assert.Equal("edit",binding.GetProperty("mode").GetString());Assert.Equal("g1",json.RootElement.GetProperty("generation").GetString());
    }
    private sealed class Handler(string method):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            Assert.Equal("secret",r.Headers.Authorization?.Parameter);using var body=JsonDocument.Parse(await r.Content!.ReadAsByteArrayAsync(ct));Assert.Equal(method,body.RootElement.GetProperty("method").GetString());Assert.Empty(body.RootElement.GetProperty("arguments").EnumerateObject());
            return new(HttpStatusCode.OK){Content=new StringContent("""{"generation":"g1","actions":[],"keymap":{"modes":[{"name":"edit"}],"bindings":[{"mode":"default"},{"mode":"edit"}]}}""")};
        }
    }
}
