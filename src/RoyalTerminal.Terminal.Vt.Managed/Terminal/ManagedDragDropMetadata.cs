// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

// Ghostty kitty/dnd_command.zig: strict metadata, last duplicate wins, unsigned
// decimal magnitude with wrapping signed coordinates, and a permitted final ':'.
internal struct ManagedDragDropMetadata
{
    internal byte Type;
    internal bool More;
    internal uint Client, Operation;
    internal int X, Y, PixelX, PixelY;

    internal static bool TryParse(ReadOnlySpan<byte> data, out ManagedDragDropMetadata result)
    {
        result = default;
        int offset = 0;
        while (offset < data.Length)
        {
            byte key = data[offset++];
            if (offset >= data.Length || data[offset++] != '=' || offset == data.Length) return false;
            if (key == 't')
            {
                byte type = data[offset++];
                if ("aAmMrRopPeEkq"u8.IndexOf(type) < 0) return false;
                result.Type = type;
            }
            else
            {
                bool signed = key is (byte)'x' or (byte)'y' or (byte)'X' or (byte)'Y';
                if (!signed && key is not ((byte)'m' or (byte)'i' or (byte)'o')) return false;
                bool negative = signed && data[offset] == '-';
                if (negative) offset++;
                int start = offset;
                ulong magnitude = 0;
                while (offset < data.Length && offset - start < 10 && data[offset] is >= (byte)'0' and <= (byte)'9')
                    magnitude = magnitude * 10 + (uint)(data[offset++] - '0');
                if (offset == start || magnitude > uint.MaxValue) return false;
                uint value = (uint)magnitude;
                int coordinate = negative ? unchecked(-(int)value) : unchecked((int)value);
                switch (key)
                {
                    case (byte)'m': result.More = value != 0; break;
                    case (byte)'i': result.Client = value; break;
                    case (byte)'o': result.Operation = value; break;
                    case (byte)'x': result.X = coordinate; break;
                    case (byte)'y': result.Y = coordinate; break;
                    case (byte)'X': result.PixelX = coordinate; break;
                    case (byte)'Y': result.PixelY = coordinate; break;
                }
            }
            if (offset < data.Length && data[offset++] != ':') return false;
        }
        return true;
    }
}
