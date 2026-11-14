using System.Net;
using System.Text;
using System.Text.Json;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;

public sealed class MultiplexingApiClientTests
{
    [Fact]
    public async Task SendsScopedBearerAndSourceGeneratedApiCall()
    {
        string? authorization = null;
        string? path = null;
        string? requestBody = null;
        using HttpClient http = new(new Handler(async request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            path = request.RequestUri!.AbsolutePath;
            requestBody = await request.Content!.ReadAsStringAsync();
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }));
        MultiplexingApiClient client = new(http, new Uri("https://example.test/mux/"), "credential");
        using JsonDocument arguments = JsonDocument.Parse("{\"label\":\"test\"}");
        JsonElement result = await client.CallAsync("session.new", arguments.RootElement);
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("Bearer credential", authorization);
        Assert.Equal("/mux/api/call", path);
        using JsonDocument body = JsonDocument.Parse(requestBody!);
        Assert.Equal("session.new", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("test", body.RootElement.GetProperty("arguments").GetProperty("label").GetString());
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData("https://other.test/api/sessions")]
    [InlineData("//other.test/api/sessions")]
    [InlineData("api/../secrets")]
    [InlineData("api/%2e%2e/secrets")]
    [InlineData("api\\sessions")]
    [InlineData("api/sessions#fragment")]
    public async Task RejectsPathsThatCouldEscapeTheConfiguredApi(string path)
    {
        using HttpClient http = new(new Handler(_ => throw new InvalidOperationException("Network must not be called.")));
        MultiplexingApiClient client = new(http, new Uri("https://example.test/"), "credential");
        await Assert.ThrowsAsync<ArgumentException>(async () => await client.SendAsync(HttpMethod.Get, path));
    }

    [Fact]
    public async Task FailureIncludesStatusAndCodeButNotPotentiallySensitiveBody()
    {
        using HttpClient http = new(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
        { Content = new StringContent("{\"code\":\"Conflict\",\"message\":\"secret-terminal-text\"}") })));
        MultiplexingApiClient client = new(http, new Uri("https://example.test/"), "credential");
        MultiplexingApiException error = await Assert.ThrowsAsync<MultiplexingApiException>(
            async () => await client.SendAsync(HttpMethod.Get, "api/sessions"));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("Conflict", error.Code);
        Assert.DoesNotContain("secret-terminal-text", error.ToString());
        Assert.DoesNotContain("credential", error.ToString());
    }

    [Fact]
    public async Task EmptyAndBoundedResponsesHaveDeterministicBehavior()
    {
        using HttpClient emptyHttp = new(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))));
        MultiplexingApiClient empty = new(emptyHttp, new Uri("https://example.test/"), "credential");
        Assert.Equal(JsonValueKind.Null, (await empty.SendAsync(HttpMethod.Delete, "api/sessions/one")).ValueKind);

        using HttpClient largeHttp = new(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(new string(' ', 2048), Encoding.UTF8, "application/json") })));
        MultiplexingApiClient bounded = new(largeHttp, new Uri("https://example.test/"), "credential", 1024);
        await Assert.ThrowsAsync<IOException>(async () => await bounded.GetMethodsAsync());
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handle(request);
    }
}
