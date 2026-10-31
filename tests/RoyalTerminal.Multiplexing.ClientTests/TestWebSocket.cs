using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.ClientTests;

internal sealed class TestWebSocket : WebSocket
{
    private readonly Channel<(byte[] Data, bool End)> _incoming = Channel.CreateUnbounded<(byte[], bool)>();
    private readonly List<byte[]> _sent = [];
    private WebSocketState _state = WebSocketState.Open;

    public IReadOnlyList<byte[]> Sent { get { lock (_sent) { return _sent.ToArray(); } } }
    public bool Disposed { get; private set; }
    public TaskCompletionSource? SendGate { get; set; }
    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;
    public override WebSocketState State => _state;

    public void Queue(string json) => _incoming.Writer.TryWrite((Encoding.UTF8.GetBytes(json), true));
    public void QueueFragment(string json, bool end) => _incoming.Writer.TryWrite((Encoding.UTF8.GetBytes(json), end));
    public void RemoteClose() => _incoming.Writer.TryComplete();
    public void BreakConnection() => _incoming.Writer.TryComplete(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
    public override void Abort() => _state = WebSocketState.Aborted;
    public override void Dispose() { Disposed = true; _state = WebSocketState.Closed; }
    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    { _state = WebSocketState.Closed; return Task.CompletedTask; }
    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    { _state = WebSocketState.CloseSent; return Task.CompletedTask; }
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        if (!await _incoming.Reader.WaitToReadAsync(cancellationToken)) return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true);
        (byte[] data, bool end) = await _incoming.Reader.ReadAsync(cancellationToken);
        if (data.Length > buffer.Count) throw new InvalidOperationException("The test frame exceeds the receive buffer.");
        data.CopyTo(buffer.AsSpan()); return new WebSocketReceiveResult(data.Length,WebSocketMessageType.Text,end);
    }
    public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        if (SendGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
        lock (_sent) { _sent.Add(buffer.ToArray()); }
    }
}
internal sealed class TestSocketConnector(TestWebSocket socket) : IMultiplexingSocketConnector
{
    public Uri? Endpoint { get; private set; }
    public string? Token { get; private set; }
    public ValueTask<WebSocket> ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Endpoint = endpoint; Token = accessToken; return ValueTask.FromResult<WebSocket>(socket);
    }
}
