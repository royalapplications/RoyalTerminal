using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using RoyalTerminal.Multiplexing.Server;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class WorkspacePushTests
{
    [Theory]
    [InlineData("https://127.0.0.1/api/push")][InlineData("https://web.push.apple.com.attacker.test/a-long-token")][InlineData("http://fcm.googleapis.com/fcm/send/example")][InlineData("https://user:secret@web.push.apple.com/long-token")][InlineData("https://fcm.googleapis.com:8443/fcm/send/example")][InlineData("https://fcm.googleapis.com/other-service")]
    public void RejectsNonProviderDestinations(string value)=>Assert.Throws<ArgumentException>(()=>VapidSender.Validate(value));
    [Fact]
    public async Task BellAfterEnrollmentProducesOneEmptySignedPostAndRevocationStopsIt()
    {
        var factory=new Factory();var inspection=new InspectingTerminalTransportFactory(factory);await using var broker=new MultiplexerBroker(inspection);
        var w=new WorkspaceOperations(broker,inspection);var s=broker.CreateSession("private");var b=await w.CreateBlockAsync(s.Id,new("pty"),default);
        var tokens=new AccessTokens(new string('a',40));var share=tokens.Create(s.Id,new("observer"));var grant=tokens.Resolve(share.Token)!;var clock=new Clock();
        var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var capture=new Capture();using var sender=new VapidSender(key,"mailto:ops@example.org",capture);using var push=new WorkspacePush(w,tokens,sender,clock);
        const string url="https://web.push.apple.com/opaque-subscription";
        factory.Transport.Emit("\a");var receipt=await push.SubscribeAsync(new(s.Id,b.Id,url),grant,default);Assert.Equal(receipt,await push.SubscribeAsync(new(s.Id,b.Id,url),grant,default));await push.PollAsync(default);Assert.Empty(capture.Headers);
        factory.Transport.Emit("\u001b]777;notify;Private customer;confidential command\a");await push.PollAsync(default);string auth=Assert.Single(capture.Headers);
        var values=auth[6..].Split(", ");Assert.Equal("k="+sender.PublicKey,values[1]);var parts=values[0][2..].Split('.');Assert.True(key.VerifyData(Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]),WebEncoders.Base64UrlDecode(parts[2]),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        using var claims=JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));Assert.Equal("https://web.push.apple.com",claims.RootElement.GetProperty("aud").GetString());Assert.Equal(clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds(),claims.RootElement.GetProperty("exp").GetInt64());
        factory.Transport.Emit("\a\a");await push.PollAsync(default);Assert.Single(capture.Headers);clock.Advance();await push.PollAsync(default);Assert.Equal(2,capture.Headers.Count);
        tokens.Revoke(s.Id,share.Id);clock.Advance();factory.Transport.Emit("\a");await push.PollAsync(default);Assert.Equal(2,capture.Headers.Count);Assert.Empty(factory.Transport.Input);
    }
    private sealed class Clock:TimeProvider {private DateTimeOffset _now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>_now;internal void Advance()=>_now=_now.AddMinutes(1);}
    private sealed class Capture:HttpMessageHandler
    {internal readonly List<string> Headers=[];protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Assert.Equal(HttpMethod.Post,r.Method);Assert.Empty(await r.Content!.ReadAsByteArrayAsync(ct));Assert.Equal("60",r.Headers.GetValues("TTL").Single());Headers.Add(r.Headers.GetValues("Authorization").Single());return new(HttpStatusCode.Created);}}
    private sealed class Factory:ITerminalTransportFactory {internal readonly Wire Transport=new();public ITerminalTransport Create(ITerminalTransportOptions options)=>Transport;}
    private sealed class Wire:ITerminalTransport
    {public event Action<byte[],int>? DataReceived;public event Action<int>? ProcessExited;public bool IsRunning{get;private set;}internal readonly List<byte> Input=[];internal void Emit(string s){var bytes=Encoding.UTF8.GetBytes(s);DataReceived?.Invoke(bytes,bytes.Length);}public void SendInput(ReadOnlySpan<byte> data)=>Input.AddRange(data.ToArray());public void Resize(TerminalSessionDimensions d){}public ValueTask StartAsync(ITerminalTransportOptions o,CancellationToken t=default){IsRunning=true;return ValueTask.CompletedTask;}public ValueTask StopAsync(){IsRunning=false;ProcessExited?.Invoke(0);return ValueTask.CompletedTask;}public void Dispose(){} }
}
