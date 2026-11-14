// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net.WebSockets;

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

/// <summary>Creates an authenticated socket, allowing hosts to supply a controlled network boundary.</summary>
public interface IMultiplexingSocketConnector
{
    /// <summary>Connects a socket. Ownership transfers to the caller on success.</summary>
    ValueTask<WebSocket> ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken);
}

/// <summary>Connects using the system TLS trust store and an HTTP Authorization header.</summary>
public sealed class MultiplexingSocketConnector : IMultiplexingSocketConnector
{
    /// <inheritdoc />
    public async ValueTask<WebSocket> ConnectAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken)
    {
        ClientWebSocket socket = new();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + accessToken);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
