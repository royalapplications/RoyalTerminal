// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Net;

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

internal static class MultiplexingEndpoint
{
    public static Uri Normalize(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("Supply an HTTP(S) base URI without credentials, query or fragment.", nameof(endpoint));
        // Both HTTP requests and WS authentication frames carry the credential. Never do DNS
        // lookups to decide whether cleartext is safe. The Unix transport uses http://localhost/.
        string host = endpoint.IdnHost.Trim('[', ']');
        if (endpoint.Scheme == Uri.UriSchemeHttp && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !(IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip)))
            throw new ArgumentException("Remote workspace credentials require HTTPS. Plain HTTP is allowed only for localhost or a loopback IP address.", nameof(endpoint));
        return new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
    }

    public static Uri Stream(MultiplexingTransportOptions options)
    {
        Uri endpoint = Normalize(options.Endpoint);
        string role = options.Observer ? "observer" : "controller";
        Uri uri = new(endpoint, $"api/sessions/{options.SessionId:D}/blocks/{options.BlockId:D}/stream?after={options.AfterSequence}&role={role}");
        return new UriBuilder(uri) { Scheme = endpoint.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" }.Uri;
    }
}
