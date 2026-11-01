// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Net;
using System.Security.Cryptography;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Multiplexing.Server;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class PushRetryTests
{
    [Theory]
    [InlineData(429, false, 2)] [InlineData(503, false, 2)] [InlineData(408, false, 2)]
    [InlineData(-1, false, 2)] [InlineData(-2, false, 2)] [InlineData(410, false, 1)]
    [InlineData(503, true, 1)] [InlineData(201, false, 1)]
    public async Task FinalExitCanRetryWithoutAnotherEventButNeverAfterRevocation(int firstResponse, bool revoke, int expected)
    {
        var wire = new Wire();
        var inspections = new InspectingTerminalTransportFactory(wire);
        await using var broker = new MultiplexerBroker(inspections);
        var operations = new WorkspaceOperations(broker, inspections);
        var s = broker.CreateSession("private");
        var b = await operations.CreateBlockAsync(s.Id, new("shell"), default);
        var tokens = new AccessTokens(new string('x', 40));
        var share = tokens.Create(s.Id, new("observer"));
        var clock = new Clock();
        var provider = new Provider(firstResponse);
        using var sender = new VapidSender(ECDsa.Create(ECCurve.NamedCurves.nistP256), "mailto:admin@example.test", provider);
        using var service = new WorkspacePush(operations, tokens, sender, clock);
        await service.SubscribeAsync(new(s.Id, b.Id, "https://web.push.apple.com/opaque-token"), tokens.Resolve(share.Token)!, default);
        wire.Exit(37);
        await service.PollAsync(default);
        Assert.Equal(1, provider.Calls);
        if (revoke) tokens.Revoke(s.Id, share.Id);
        clock.Advance(9); await service.PollAsync(default); Assert.Equal(1, provider.Calls);
        clock.Advance(1); await service.PollAsync(default);
        clock.Advance(600); await service.PollAsync(default);
        Assert.Equal(expected, provider.Calls);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
    private sealed class Provider(int first) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
        {
            Assert.Equal(0L, message.Content!.Headers.ContentLength);
            int code = ++Calls == 1 ? first : 201;
            if (code == -1) throw new HttpRequestException("Simulated transport failure");
            if (code == -2) throw new OperationCanceledException("Simulated provider deadline");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)code));
        }
    }
    private sealed class Wire : ITerminalTransport, ITerminalTransportFactory
    {
        public event Action<byte[], int>? DataReceived { add { } remove { } }
        public event Action<int>? ProcessExited;
        public bool IsRunning { get; private set; }
        public ITerminalTransport Create(ITerminalTransportOptions options) => this;
        public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken ct = default) { IsRunning = true; return ValueTask.CompletedTask; }
        public void Exit(int code) { IsRunning = false; ProcessExited?.Invoke(code); }
        public void SendInput(ReadOnlySpan<byte> bytes) => throw new InvalidOperationException("Unexpected input");
        public void Resize(TerminalSessionDimensions dimensions) { }
        public ValueTask StopAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
