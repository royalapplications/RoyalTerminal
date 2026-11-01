// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Net.Sockets;
using System.Net.WebSockets;

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

/// <summary>Private local broker transport. A missing socket never falls back to a TCP listener.</summary>
public sealed class UnixWorkspaceEndpoint : IMultiplexingSocketConnector, IDisposable
{
    private readonly UnixDomainSocketEndPoint _endpoint;
    private readonly HttpMessageInvoker _upgrades;
    public UnixWorkspaceEndpoint(string path)
    {
        if (!Socket.OSSupportsUnixDomainSockets || OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Unix workspace sockets require a Unix host.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0')) throw new ArgumentException("Specify an absolute socket path.", nameof(path));
        _endpoint = new(path);
        _upgrades = new(CreateHandler());
    }
    /// <summary>Creates an independently owned HTTP handler for the synthetic http://localhost/ origin.</summary>
    public HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        ConnectCallback = async (context, ct) =>
        {
            if (context.DnsEndPoint.Host != "localhost" || context.DnsEndPoint.Port != 80) throw new HttpRequestException("Invalid authority for a private workspace socket.");
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await socket.ConnectAsync(_endpoint, ct).ConfigureAwait(false); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        },
    };
    /// <inheritdoc />
    public async ValueTask<WebSocket> ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
    {
        if (endpoint.Scheme != "ws" || endpoint.Host != "localhost" || endpoint.Port != 80 || endpoint.UserInfo.Length != 0) throw new ArgumentException("Unix attachments only accept the local WebSocket authority.");
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
            await socket.ConnectAsync(endpoint, _upgrades, cancellationToken).ConfigureAwait(false); return socket;
        }
        catch { socket.Dispose(); throw; }
    }
    /// <inheritdoc />
    public void Dispose() => _upgrades.Dispose();
}
