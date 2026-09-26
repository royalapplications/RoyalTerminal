// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Single-command streaming parser. Control fields have fixed storage; only
/// encoded payload counts against the bound. Failure discards this command,
/// never a previously accepted multi-command image transmission.
/// </summary>
internal sealed class ManagedKittyGraphicsParser
{
    [InlineArray(11)]
    private struct Temporary { private byte _element0; }

    private readonly int _limit;
    private ManagedKittyGraphicsCommand? _command = new();
    private Temporary _temporary;
    private int _count;
    private byte _key;
    private ControlState _state;
    private byte[] _payload = [];
    private int _length;

    internal ManagedKittyGraphicsParser(int maxPayloadBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxPayloadBytes);
        _limit = maxPayloadBytes;
    }

    internal bool TryAppend(ReadOnlySpan<byte> input)
    {
        if (_command is null) return false;
        int offset = 0;
        for (; offset < input.Length && _state != ControlState.Data; offset++)
        {
            byte value = input[offset];
            switch (_state)
            {
                case ControlState.Key when value == '=':
                    _state = _count == 1 ? ControlState.Value : ControlState.IgnoreValue;
                    if (_count == 1) _key = _temporary[0];
                    _count = 0;
                    break;
                case ControlState.Key when value == ';':
                    _state = ControlState.Data;
                    break;
                case ControlState.Key:
                case ControlState.Value when value is not ((byte)',' or (byte)';'):
                    if (_count < 11) _temporary[_count++] = value;
                    else
                    {
                        _count = 0;
                        _state = _state == ControlState.Key ? ControlState.IgnoreKey : ControlState.IgnoreValue;
                    }
                    break;
                case ControlState.IgnoreKey:
                    if (value == '=') _state = ControlState.IgnoreValue;
                    break;
                case ControlState.IgnoreValue:
                    if (value == ',') _state = ControlState.IgnoreKey;
                    else if (value == ';') _state = ControlState.Data;
                    break;
                case ControlState.Value:
                    if (!_command.FinishValue(_key, _temporary[.._count])) return Reject();
                    _count = 0;
                    _state = value == ',' ? ControlState.Key : ControlState.Data;
                    break;
            }
        }

        ReadOnlySpan<byte> remaining = input[offset..];
        if (remaining.Length > _limit - _length) return Reject();
        int required = _length + remaining.Length;
        if (required > _payload.Length)
        {
            int capacity = (int)Math.Min(_limit, Math.Max(required, Math.Max(256L, _payload.Length * 2L)));
            Array.Resize(ref _payload, capacity);
        }
        remaining.CopyTo(_payload.AsSpan(_length));
        _length = required;
        return true;
    }

    internal bool TryComplete([NotNullWhen(true)] out ManagedKittyGraphicsCommand? result)
    {
        result = null;
        if (_command is null) return false;
        if (_state is ControlState.Key or ControlState.IgnoreKey ||
            _state == ControlState.Value && !_command.FinishValue(_key, _temporary[.._count]) ||
            !_command.Validate()) return Reject();

        // Ownership is transferred once. Decode over the encoded bytes rather
        // than allocating a second maximum-size array or retaining an APC list.
        if (!TryDecodePayload(_payload.AsSpan(0, _length), out int written))
            return Reject();
        _command.SetData(_payload.AsMemory(0, written));
        result = _command;
        _command = null;
        _payload = [];
        _length = 0;
        return true;
    }

    private static bool TryDecodePayload(Span<byte> payload, out int written)
    {
        written = 0;
        // Ghostty's default simdutf decoder follows forgiving-base64: ASCII
        // whitespace is ignored, final padding is optional and unused tail
        // bits need not be zero. Clipboard strict-decoding policy is unrelated.
        int whitespace = payload.IndexOfAny(" \t\n\r\f"u8);
        if (whitespace >= 0)
        {
            int length = whitespace;
            for (int i = whitespace + 1; i < payload.Length; i++)
                if (payload[i] is not ((byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f'))
                    payload[length++] = payload[i];
            payload = payload[..length];
        }

        int padding = 0;
        while (padding < payload.Length && payload[^(padding + 1)] == '=') padding++;
        if (padding > 2 || padding > 0 && payload.Length % 4 != 0) return false;
        payload = payload[..(payload.Length - padding)];
        int tailLength = payload.Length % 4;
        if (tailLength == 1 || padding > 0 && padding != 4 - tailLength || payload.Contains((byte)'='))
            return false;

        int bulkLength = payload.Length - tailLength;
        // Read the tail before the bulk decoder overwrites the prefix. Only
        // two or three unpadded characters need scalar handling; full groups
        // retain the runtime's vectorized, in-place decoder.
        int tail = 0;
        for (int i = bulkLength; i < payload.Length; i++)
        {
            int value = payload[i] switch
            {
                >= (byte)'A' and <= (byte)'Z' => payload[i] - 'A',
                >= (byte)'a' and <= (byte)'z' => payload[i] - 'a' + 26,
                >= (byte)'0' and <= (byte)'9' => payload[i] - '0' + 52,
                (byte)'+' => 62, (byte)'/' => 63, _ => -1,
            };
            if (value < 0) return false;
            tail = (tail << 6) | value;
        }
        if (Base64.DecodeFromUtf8InPlace(payload[..bulkLength], out written) != OperationStatus.Done)
            return false;
        if (tailLength >= 2) payload[written++] = (byte)(tail >> (tailLength == 2 ? 4 : 10));
        if (tailLength == 3) payload[written++] = (byte)(tail >> 2);
        return true;
    }

    private bool Reject()
    {
        _command = null;
        _payload = [];
        _length = 0;
        return false;
    }

    private enum ControlState : byte { Key, IgnoreKey, Value, IgnoreValue, Data }
}
