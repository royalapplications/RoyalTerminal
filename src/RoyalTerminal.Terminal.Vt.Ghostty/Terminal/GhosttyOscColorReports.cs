// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Applies the host's eight-bit report policy to complete native color replies only.</summary>
internal static class GhosttyOscColorReports
{
    internal static int GetEightBitLength(ReadOnlySpan<byte> source)
    {
        int length = source.Length;
        for (int i = 0; i < source.Length;)
        {
            if (!TryReport(source[i..], out _, out int size)) return source.Length;
            length -= 6;
            i += size;
        }
        return length;
    }

    internal static int WriteEightBit(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        // A native color operation writes one complete batch. Do not search
        // inside arbitrary title/clipboard/device replies for color-like bytes.
        if (GetEightBitLength(source) == source.Length)
        {
            source.CopyTo(destination);
            return source.Length;
        }
        int written = 0;
        for (int i = 0; i < source.Length;)
        {
            if (!TryReport(source[i..], out int channels, out int size))
            {
                destination[written++] = source[i++];
                continue;
            }
            source.Slice(i, channels).CopyTo(destination[written..]);
            written += channels;
            for (int channel = 0; channel < 3; channel++)
            {
                source.Slice(i + channels + channel * 5, 2).CopyTo(destination[written..]);
                written += 2;
                if (channel < 2) destination[written++] = (byte)'/';
            }
            ReadOnlySpan<byte> terminator = source.Slice(i + channels + 14, size - channels - 14);
            terminator.CopyTo(destination[written..]);
            written += terminator.Length;
            i += size;
        }
        return written;
    }

    private static bool TryReport(ReadOnlySpan<byte> source, out int channels, out int size)
    {
        channels = size = 0;
        int color;
        if (source.StartsWith("\u001b]4;"u8))
        {
            int end = source[4..].IndexOf((byte)';');
            if (end is < 1 or > 3) return false;
            int index = 0;
            foreach (byte digit in source.Slice(4, end))
            {
                if (digit is < (byte)'0' or > (byte)'9') return false;
                index = index * 10 + digit - '0';
            }
            if (index > 255) return false;
            color = 5 + end;
        }
        else if (source.StartsWith("\u001b]10;"u8) || source.StartsWith("\u001b]11;"u8) || source.StartsWith("\u001b]12;"u8)) color = 5;
        else return false;
        if (!source[color..].StartsWith("rgb:"u8)) return false;
        channels = color + 4;
        if (source.Length < channels + 15) return false;
        for (int i = 0; i < 3; i++)
        {
            ReadOnlySpan<byte> channel = source.Slice(channels + i * 5, 4);
            // Native expands an eight-bit channel by repeating its two hex
            // digits. Do not rewrite arbitrary/non-native sixteen-bit values.
            if (!Hex(channel[0]) || !Hex(channel[1]) || channel[0] != channel[2] || channel[1] != channel[3]) return false;
            if (i < 2 && source[channels + i * 5 + 4] != '/') return false;
        }
        int endOffset = channels + 14;
        if (source[endOffset] == 7) size = endOffset + 1;
        else if (source[endOffset..].StartsWith("\u001b\\"u8)) size = endOffset + 2;
        else return false;
        return true;
    }

    private static bool Hex(byte value) => value is >= (byte)'0' and <= (byte)'9' or
        >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F';
}
