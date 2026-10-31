// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

internal static class MultiplexingEndpoint
{
    public static Uri Normalize(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            throw new ArgumentException("The endpoint must be an absolute HTTP(S) base URI without credentials, query, or fragment.", nameof(endpoint));
        }

        return new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
    }

    public static Uri Stream(MultiplexingTransportOptions options)
    {
        Uri endpoint = Normalize(options.Endpoint);
        string role = options.Observer ? "observer" : "controller";
        Uri uri = new(endpoint, $"api/sessions/{options.SessionId:D}/blocks/{options.BlockId:D}/stream?after={options.AfterSequence}&role={role}");
        UriBuilder builder = new(uri) { Scheme = endpoint.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" };
        return builder.Uri;
    }
}
