using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.ClientTests;
public sealed class CredentialTransportTests
{
    [Theory]
    [InlineData("http://workspace.example/")]
    [InlineData("http://127.0.0.1.example/")]
    [InlineData("http://192.168.1.12/")]
    [InlineData("http://[fd00::1]/")]
    public void RejectsRemoteCleartextBeforeBuildingAnAuthenticatedRequest(string endpoint)
    {
        using var http = new HttpClient();
        var error = Assert.Throws<ArgumentException>(() => new MultiplexingApiClient(http, new(endpoint), "must-not-appear-in-errors"));
        Assert.DoesNotContain("must-not-appear", error.Message);
    }
    [Theory]
    [InlineData("http://localhost:7331/")]
    [InlineData("http://127.0.0.1:7331/")]
    [InlineData("http://[::1]:7331/")]
    [InlineData("https://workspace.example/prefix/")]
    public void PreservesEncryptedRemoteAndLocalEndpoints(string endpoint)
    {
        using var http = new HttpClient();
        _ = new MultiplexingApiClient(http, new(endpoint), "credential");
    }
}
