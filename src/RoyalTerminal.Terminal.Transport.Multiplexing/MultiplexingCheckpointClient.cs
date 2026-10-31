// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Net.Http.Headers;

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

/// <summary>A complete Ghostty v1 parser checkpoint and the VT cursor corresponding to the exact cut.</summary>
public sealed record MultiplexingCheckpoint(Guid SessionId, Guid BlockId, long Sequence, int? ExitCode, ReadOnlyMemory<byte> Binary);

/// <summary>Binary checkpoint protocol, separate from JSON response and WebSocket frame limits.</summary>
public static class MultiplexingCheckpointClient
{
    /// <summary>Fetches an owned, bounded binary checkpoint. Restore its parser before attaching at Sequence;
    /// when the subsequent suffix has already expired, discard this checkpoint and request a new cut.</summary>
    public static async ValueTask<MultiplexingCheckpoint> ReadAsync(HttpClient http, MultiplexingTransportOptions options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(http); ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AccessToken);
        if (options.SessionId == Guid.Empty || options.BlockId == Guid.Empty) throw new ArgumentException("Checkpoint requires nonempty identities.", nameof(options));
        var origin = MultiplexingEndpoint.Normalize(options.Endpoint);
        var uri = new Uri(origin,$"api/sessions/{options.SessionId:D}/blocks/{options.BlockId:D}/checkpoint");
        using HttpRequestMessage request = new(HttpMethod.Get,uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",options.AccessToken);
        request.Headers.Accept.Add(new("application/vnd.royalterminal.ghostty-snapshot-v1"));
        using var response = await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType,"application/vnd.royalterminal.ghostty-snapshot-v1",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected checkpoint format.");
        if (!Guid.TryParseExact(Header("X-Workspace-Session"),"D",out var s) || s != options.SessionId || !Guid.TryParseExact(Header("X-Workspace-Block"),"D",out var b) || b != options.BlockId)
            throw new InvalidDataException("The checkpoint belongs to a different terminal.");
        if (!long.TryParse(Header("X-Workspace-Sequence"),NumberStyles.None,CultureInfo.InvariantCulture,out long sequence) || sequence < 1) throw new InvalidDataException("Invalid checkpoint cursor.");
        int? exit = null;
        if (response.Headers.Contains("X-Workspace-Exit"))
        {
            if (!int.TryParse(Header("X-Workspace-Exit"),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int code)) throw new InvalidDataException("Invalid exit code.");
            exit = code;
        }
        if (response.Content.Headers.ContentLength is not long length || length is < 1 or > 8 * 1024 * 1024) throw new InvalidDataException("Missing or oversized checkpoint length.");
        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)length);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await using Stream body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        await body.ReadExactlyAsync(bytes,deadline.Token).ConfigureAwait(false);
        return new(s,b,sequence,exit,bytes);

        string Header(string name) => response.Headers.TryGetValues(name,out var raw) && raw.ToArray() is [var value]
            ? value : throw new InvalidDataException($"Missing or ambiguous {name} header.");
    }
}
