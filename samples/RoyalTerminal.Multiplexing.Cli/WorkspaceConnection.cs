// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net.Sockets;

namespace RoyalTerminal.Multiplexing.Cli;

internal static class WorkspaceConnection
{
    internal static HttpMessageHandler CreateHandler(string? path)
    {
        if (string.IsNullOrEmpty(path)) return new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        if (OperatingSystem.IsWindows() || !Socket.OSSupportsUnixDomainSockets) throw new PlatformNotSupportedException("This CLI's personal socket transport requires Unix domain sockets on a Unix host.");
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0')) throw new ArgumentException("--socket requires an absolute Unix socket path.");
        UnixDomainSocketEndPoint endpoint = new(path);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            ConnectCallback = async (context, token) =>
            {
                // Only the synthetic loopback origin may use this handler. No DNS query, TCP connect,
                // credential fallback or proxy is involved, even if the socket disappears mid-request.
                if (context.DnsEndPoint.Host != "localhost" || context.DnsEndPoint.Port != 80)
                    throw new HttpRequestException("Invalid HTTP authority for a local workspace socket.");
                Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try { await socket.ConnectAsync(endpoint, token).ConfigureAwait(false); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); throw; }
            },
        };
    }
}
