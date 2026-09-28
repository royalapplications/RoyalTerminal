// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace RoyalTerminal.Terminal;

// Follows Ghostty's kitty/dnd_{command,drop,response}.zig, including its local,
// eagerly captured drop contract. Windows Terminal's path paste and xterm.js's
// paste helper are not used once a client registers: data is requested explicitly
// and binary representations must never become shell input or VT output.
internal sealed class ManagedDragDropProtocol : ITerminalDragDropTarget
{
    private const int MimeListLimit = 1024 * 1024;
    private const int DataLimit = 64 * 1024 * 1024;
    private readonly ArrayBufferWriter<byte> _registration = new();
    private readonly ArrayBufferWriter<byte> _acceptance = new();
    private uint _client;
    private bool _chunking, _accepting, _hovered, _dropped;
    private ManagedDragDropMetadata _firstChunk;
    private TerminalDropOperation? _accepted;
    private string[] _offered = [];
    private byte[] _offeredPayload = [];
    private TerminalDropItem[]? _items;

    public bool IsDropRegistered { get; private set; }
    public string[] RegisteredDropMimeTypes => Encoding.UTF8.GetString(_registration.WrittenSpan).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    public TerminalDropOperation? AcceptedDropOperation => _accepting ? null : _accepted;

    internal void Handle(ReadOnlySpan<byte> command, bool bell, Action<byte[]>? response)
    {
        int separator = command.IndexOf((byte)';');
        ReadOnlySpan<byte> metadata = separator < 0 ? command : command[..separator];
        ReadOnlySpan<byte> payload = separator < 0 ? [] : command[(separator + 1)..];
        if (!ManagedDragDropMetadata.TryParse(metadata, out ManagedDragDropMetadata raw)) return;
        bool continuation = _chunking;
        ManagedDragDropMetadata meta = raw;
        if (IsDropRegistered)
        {
            if (_chunking) { meta = _firstChunk; meta.More = raw.More; }
            else if (raw.More) _firstChunk = raw;
            _chunking = raw.More;
        }
        switch (meta.Type)
        {
            case (byte)'a':
                if (meta.X == 1) return; // Remote machine identity is accepted but not advertised.
                if (!IsDropRegistered) { IsDropRegistered = true; _chunking = raw.More; _firstChunk = raw; }
                _client = meta.Client;
                if (!continuation) _registration.Clear();
                if (_registration.WrittenCount + payload.Length <= MimeListLimit) _registration.Write(payload);
                break;
            case (byte)'A': Clear(); break;
            case (byte)'m':
                if (!IsDropRegistered) break;
                if (!_accepting) { _acceptance.Clear(); _accepting = true; _accepted = Operation(meta.Operation); }
                if (_acceptance.WrittenCount + payload.Length > MimeListLimit) break;
                _acceptance.Write(payload);
                if (!meta.More) _accepting = false;
                break;
            case (byte)'r': Request(meta, bell, response); break;
            case (byte)'q': response?.Invoke(Encode("t=q", meta.Client, [], false, bell)); break;
            case (byte)'o' when meta.X == 0:
            case (byte)'p':
            case (byte)'P':
                response?.Invoke(Encode("t=E", meta.Client, "EPERM:drag out is not supported by this terminal"u8, false, bell));
                break;
        }
    }

    private void Request(ManagedDragDropMetadata meta, bool bell, Action<byte[]>? response)
    {
        uint client = IsDropRegistered ? _client : 0;
        string keys = string.Empty;
        if (meta.X != 0) keys += FormattableString.Invariant($":x={meta.X}");
        if (meta.PixelY != 0)
        {
            keys += FormattableString.Invariant($":Y={meta.PixelY}");
            response?.Invoke(Encode("t=R" + keys, client, "EINVAL:remote drop data is not supported"u8, false, bell));
        }
        else if (meta.Y != 0)
        {
            keys += FormattableString.Invariant($":y={meta.Y}");
            response?.Invoke(Encode("t=R" + keys, client, "EINVAL:remote drop data is not supported"u8, false, bell));
        }
        else if (meta.X == 0) CancelDrop();
        else if (_items is null)
            response?.Invoke(Encode("t=R" + keys, client, "ENOENT:no drop data available"u8, false, bell));
        else if (meta.X < 1 || meta.X > _items.Length)
            response?.Invoke(Encode("t=R" + keys, client, "ENOENT:drop data request index out of bounds"u8, false, bell));
        else if (response is not null)
        {
            ReadOnlySpan<byte> data = _items[meta.X - 1].Data.Span;
            if (!data.IsEmpty) response(Encode("t=r" + keys, client, data, true, bell));
            response(Encode("t=r" + keys, client, [], true, bell));
        }
    }

    public byte[] DragMove(in TerminalDropPosition position, IReadOnlyList<string> mimeTypes)
    {
        ArgumentNullException.ThrowIfNull(mimeTypes);
        if (!IsDropRegistered) return [];
        ValidateMimeTypes(mimeTypes);
        return Move(position, mimeTypes, drop: false);
    }

    public byte[] Drop(in TerminalDropPosition position, IReadOnlyList<TerminalDropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!IsDropRegistered) return [];
        int count = Math.Min(16, items.Count);
        string[] mimes = new string[count];
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            ArgumentNullException.ThrowIfNull(items[i]);
            mimes[i] = items[i].MimeType;
            total += items[i].Data.Length;
        }
        ValidateMimeTypes(mimes);
        if (total > DataLimit) throw new ArgumentException("Drop data exceeds 64 MiB.", nameof(items));
        TerminalDropItem[] copies = new TerminalDropItem[count];
        for (int i = 0; i < count; i++) copies[i] = new(mimes[i], items[i].Data.ToArray());
        byte[] response = Move(position, mimes, drop: true);
        _items = copies;
        return response;
    }

    private byte[] Move(in TerminalDropPosition position, IReadOnlyList<string> mimeTypes, bool drop)
    {
        if (!_hovered) { CancelDrop(); _hovered = true; }
        if (drop) { _dropped = true; _hovered = false; }
        bool same = _offered.Length == mimeTypes.Count;
        for (int i = 0; same && i < _offered.Length; i++) same = _offered[i] == mimeTypes[i];
        if (!same)
        {
            _offered = new string[mimeTypes.Count];
            int bytes = 0;
            for (int i = 0; i < _offered.Length; i++) { _offered[i] = mimeTypes[i]; bytes += mimeTypes[i].Length + 1; }
            _offeredPayload = new byte[bytes];
            int offset = 0;
            foreach (string mime in _offered) { offset += Encoding.ASCII.GetBytes(mime, _offeredPayload.AsSpan(offset)); _offeredPayload[offset++] = (byte)' '; }
        }
        return Encode(FormattableString.Invariant($"t={(drop ? 'M' : 'm')}:x={position.Column}:y={position.Row}:X={position.PixelX}:Y={position.PixelY}:o={(int)position.Operations & 3}"),
            _client, _offeredPayload, false, false);
    }

    public byte[] DragLeave()
    {
        if (!IsDropRegistered || _dropped) return [];
        bool hovered = _hovered;
        _hovered = false;
        _offered = [];
        _offeredPayload = [];
        return hovered ? Encode("t=m:x=-1:y=-1", _client, [], false, false) : [];
    }

    public void CancelDrop()
    {
        _items = null;
        _offered = [];
        _offeredPayload = [];
        _acceptance.Clear();
        _accepted = null;
        _accepting = _hovered = _dropped = false;
    }

    internal void ResetParser() => _chunking = false;
    internal void Clear()
    {
        CancelDrop();
        _registration.Clear();
        _chunking = IsDropRegistered = false;
        _client = 0;
    }

    private static TerminalDropOperation Operation(uint value) => value is 1 or 2 ? (TerminalDropOperation)value : TerminalDropOperation.None;

    private static void ValidateMimeTypes(IReadOnlyList<string> types)
    {
        if (types.Count > 16) throw new ArgumentException("At most 16 drop representations are allowed.", nameof(types));
        foreach (string mime in types)
        {
            if (string.IsNullOrEmpty(mime) || mime.Length > 1024) throw new ArgumentException("Invalid drop MIME type.", nameof(types));
            foreach (char c in mime) if (c < 33 || c > 126) throw new ArgumentException("Invalid drop MIME type.", nameof(types));
        }
    }

    private static byte[] Encode(string header, uint client, ReadOnlySpan<byte> payload, bool base64, bool bell)
    {
        string prefix = "\x1b]72;" + header + (client == 0 ? string.Empty : ":i=" + client.ToString(CultureInfo.InvariantCulture));
        int limit = base64 ? 3072 : 4096;
        int terminatorLength = bell ? 1 : 2;
        int count = payload.IsEmpty ? 1 : (payload.Length + limit - 1) / limit;
        int dataLength = base64 ? checked((payload.Length + 2) / 3 * 4) : payload.Length;
        byte[] output = new byte[checked((prefix.Length + terminatorLength + (payload.IsEmpty ? 0 : 5)) * count + dataLength)];
        int offset = 0;
        for (int i = 0; i < count; i++)
        {
            offset += Encoding.ASCII.GetBytes(prefix, output.AsSpan(offset));
            if (!payload.IsEmpty)
            {
                output[offset++] = (byte)':'; output[offset++] = (byte)'m'; output[offset++] = (byte)'=';
                output[offset++] = i == count - 1 ? (byte)'0' : (byte)'1'; output[offset++] = (byte)';';
                ReadOnlySpan<byte> chunk = payload.Slice(i * limit, Math.Min(limit, payload.Length - i * limit));
                if (base64)
                {
                    System.Buffers.Text.Base64.EncodeToUtf8(chunk, output.AsSpan(offset), out _, out int written);
                    offset += written;
                }
                else { chunk.CopyTo(output.AsSpan(offset)); offset += chunk.Length; }
            }
            output[offset++] = bell ? (byte)7 : (byte)27;
            if (!bell) output[offset++] = (byte)'\\';
        }
        return output;
    }
}
