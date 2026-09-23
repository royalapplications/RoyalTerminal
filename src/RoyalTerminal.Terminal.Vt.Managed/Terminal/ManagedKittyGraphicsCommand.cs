// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RoyalTerminal.Terminal;

/// <summary>Bounded, allocation-free control-field parser for Kitty graphics APC commands.</summary>
internal sealed class ManagedKittyGraphicsCommand
{
    [InlineArray(52)]
    private struct Values { private uint _element0; }

    private Values _values;
    private ulong _present;
    private ReadOnlyMemory<byte> _data;

    internal char Action => (char)Get('a', 't');
    internal uint ImageId => Get('i');
    internal uint ImageNumber => Get('I');
    internal uint PlacementId => Get('p');
    internal int Quiet => (int)Math.Min(2u, Get('q'));
    internal ReadOnlyMemory<byte> Data => _data;
    internal bool IsTransmission => Action is 'q' or 't' or 'T' or 'f';
    internal bool MoreChunks => Get('t', 'd') == 'd' && Get('m') != 0;

    internal uint Get(char key, uint fallback = 0)
    {
        int index = Index(key);
        return index >= 0 && (_present & (1UL << index)) != 0 ? _values[index] : fallback;
    }

    internal int GetSigned(char key) => unchecked((int)Get(key));

    internal static bool TryParse(ReadOnlySpan<byte> input, int maxPayloadBytes,
        [NotNullWhen(true)] out ManagedKittyGraphicsCommand? result)
    {
        result = null;
        if (maxPayloadBytes < 0) return false;
        ManagedKittyGraphicsCommand command = new();
        ControlState state = ControlState.Key;
        Span<byte> temporary = stackalloc byte[11];
        int count = 0;
        byte key = 0;
        int offset = 0;
        for (; offset < input.Length && state != ControlState.Data; offset++)
        {
            byte value = input[offset];
            switch (state)
            {
                case ControlState.Key when value == '=':
                    state = count == 1 ? ControlState.Value : ControlState.IgnoreValue;
                    if (count == 1) key = temporary[0];
                    count = 0;
                    break;
                case ControlState.Key when value == ';':
                    state = ControlState.Data;
                    break;
                case ControlState.Key:
                case ControlState.Value when value is not ((byte)',' or (byte)';'):
                    if (count < temporary.Length) temporary[count++] = value;
                    else
                    {
                        count = 0;
                        state = state == ControlState.Key ? ControlState.IgnoreKey : ControlState.IgnoreValue;
                    }
                    break;
                case ControlState.IgnoreKey:
                    if (value == '=') state = ControlState.IgnoreValue;
                    break;
                case ControlState.IgnoreValue:
                    if (value == ',') state = ControlState.IgnoreKey;
                    else if (value == ';') state = ControlState.Data;
                    break;
                case ControlState.Value:
                    if (!command.FinishValue(key, temporary[..count])) return false;
                    count = 0;
                    state = value == ',' ? ControlState.Key : ControlState.Data;
                    break;
            }
        }
        if (state is ControlState.Key or ControlState.IgnoreKey) return false;
        if (state == ControlState.Value && !command.FinishValue(key, temporary[..count])) return false;
        if (!command.Validate()) return false;

        ReadOnlySpan<byte> payload = input[offset..];
        if (payload.Length > maxPayloadBytes) return false;
        if (!payload.IsEmpty)
        {
            byte[] decoded = GC.AllocateUninitializedArray<byte>(Base64.GetMaxDecodedFromUtf8Length(payload.Length));
            if (Base64.DecodeFromUtf8(payload, decoded, out int consumed, out int written) != OperationStatus.Done || consumed != payload.Length)
                return false;
            command._data = decoded.AsMemory(0, written);
        }
        result = command;
        return true;
    }

    private bool Validate()
    {
        uint action = Get('a', 't');
        if (action is not ('q' or 't' or 'T' or 'p' or 'd' or 'f' or 'a' or 'c')) return false;
        if (IsTransmission)
        {
            if (Get('t', 'd') is not ('d' or 'f' or 't' or 's')) return false;
            if (Get('o', 'z') != 'z') return false;
        }
        if (action == 'd' && Get('d', 'a') is not ('a' or 'A' or 'i' or 'I' or 'n' or 'N' or 'c' or 'C' or
            'f' or 'F' or 'p' or 'P' or 'q' or 'Q' or 'r' or 'R' or 'x' or 'X' or 'y' or 'Y' or 'z' or 'Z')) return false;
        return true;
    }

    private bool FinishValue(byte key, ReadOnlySpan<byte> text)
    {
        uint value;
        if (text.Length == 1 && text[0] is < (byte)'0' or > (byte)'9') value = text[0];
        else
        {
            bool signed = key is (byte)'z' or (byte)'H' or (byte)'V';
            bool negative = text.Length > 0 && text[0] == '-';
            if (negative && !signed) return false;
            if (text.Length > 0 && text[0] is (byte)'+' or (byte)'-') text = text[1..];
            if (text.IsEmpty) return false;
            ulong magnitude = 0;
            ulong limit = signed ? (negative ? 2147483648UL : int.MaxValue) : uint.MaxValue;
            foreach (byte digit in text)
            {
                if (digit is < (byte)'0' or > (byte)'9') return false;
                magnitude = magnitude * 10 + digit - '0';
                if (magnitude > limit) return false;
            }
            value = negative ? unchecked((uint)-(long)magnitude) : (uint)magnitude;
        }
        int index = Index((char)key);
        if (index >= 0)
        {
            _values[index] = value;
            _present |= 1UL << index;
        }
        return true;
    }

    private static int Index(char key) => key switch
    {
        >= 'a' and <= 'z' => key - 'a', >= 'A' and <= 'Z' => key - 'A' + 26, _ => -1,
    };

    private enum ControlState : byte { Key, IgnoreKey, Value, IgnoreValue, Data }
}
