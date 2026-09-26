// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace RoyalTerminal.Terminal;

/// <summary>Owned Kitty graphics command with a fixed-size control-field table.</summary>
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
        ManagedKittyGraphicsParser parser = new(maxPayloadBytes);
        return parser.TryAppend(input) && parser.TryComplete(out result);
    }

    internal void SetData(ReadOnlyMemory<byte> data) => _data = data;

    internal bool Validate()
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

    internal bool FinishValue(byte key, ReadOnlySpan<byte> text)
    {
        uint value;
        if (text.Length == 1 && text[0] is < (byte)'0' or > (byte)'9') value = text[0];
        else
        {
            bool signed = key is (byte)'z' or (byte)'H' or (byte)'V';
            bool negative = text.Length > 0 && text[0] == '-';
            if (text.Length > 0 && text[0] is (byte)'+' or (byte)'-') text = text[1..];
            if (text.IsEmpty || text[0] == '_' || text[^1] == '_') return false;
            ulong magnitude = 0;
            // Ghostty uses Zig parseInt: interior underscores are ignored and
            // unsigned negative zero is valid, but any negative magnitude fails.
            ulong limit = signed ? (negative ? 2147483648UL : int.MaxValue) : negative ? 0 : uint.MaxValue;
            foreach (byte digit in text)
            {
                if (digit == '_') continue;
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
}
