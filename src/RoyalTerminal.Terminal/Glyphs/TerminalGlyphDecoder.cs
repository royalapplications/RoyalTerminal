// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace RoyalTerminal.Terminal.Glyphs;

/// <summary>Decodes the bounded, unhinted simple-glyph subset used by Ghostty's Glyph Protocol.</summary>
public static class TerminalGlyphDecoder
{
    /// <summary>Maximum binary payload size and maximum individual decoded allocation.</summary>
    public const int MaxPayloadBytes = 64 * 1024;

    // Ghostty's Outline.Point is two i32 coordinates and one bool (12 bytes
    // including alignment). Match that expansion budget independently of CLR layout.
    private const int MaxPoints = MaxPayloadBytes / 12;

    /// <summary>
    /// Validates and decodes a binary glyf record. Composite/hinted glyphs are
    /// rejected; trailing bytes and header-only empty glyphs match Ghostty.
    /// Failure returns no partial outline. Input is neither mutated nor retained.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> payload,
        [NotNullWhen(true)] out TerminalGlyphOutline? outline, out TerminalGlyphDecodeError error)
    {
        outline = null;
        error = TerminalGlyphDecodeError.MalformedPayload;
        if (payload.Length > MaxPayloadBytes)
        {
            error = TerminalGlyphDecodeError.PayloadTooLarge;
            return false;
        }
        if (payload.Length < 10) return false;
        int contourCount = BinaryPrimitives.ReadInt16BigEndian(payload);
        if (contourCount < 0)
        {
            error = TerminalGlyphDecodeError.CompositeUnsupported;
            return false;
        }
        // Bounding-box hints are deliberately not trusted: the outline's actual
        // coordinates are authoritative, as in Ghostty's glyf decoder.
        int offset = 10;
        if (contourCount == 0 && payload.Length < 12)
        {
            outline = new([], []);
            error = TerminalGlyphDecodeError.None;
            return true;
        }
        if (payload.Length - offset < contourCount * 2 + 2) return false;
        int previous = -1;
        for (int i = 0; i < contourCount; i++)
        {
            int end = BinaryPrimitives.ReadUInt16BigEndian(payload[offset..]);
            offset += 2;
            if (end <= previous) return false;
            previous = end;
        }
        if (BinaryPrimitives.ReadUInt16BigEndian(payload[offset..]) != 0)
        {
            error = TerminalGlyphDecodeError.HintingUnsupported;
            return false;
        }
        offset += 2;
        int pointCount = previous + 1;
        if (pointCount > MaxPoints)
        {
            error = TerminalGlyphDecodeError.PayloadTooLarge;
            return false;
        }

        // Bound stack usage to ~5.4 KiB. Validate the complete record before
        // allocating persistent storage; malformed traffic need not allocate.
        Span<byte> flags = stackalloc byte[pointCount];
        int point = 0;
        int xBytes = 0, yBytes = 0;
        while (point < pointCount)
        {
            if (offset == payload.Length) return false;
            byte flag = payload[offset++];
            int repeat = 1;
            if ((flag & 8) != 0)
            {
                if (offset == payload.Length) return false;
                repeat += payload[offset++];
            }
            if (repeat > pointCount - point) return false;
            flags.Slice(point, repeat).Fill(flag);
            point += repeat;
            xBytes += repeat * CoordinateBytes(flag, 2, 16);
            yBytes += repeat * CoordinateBytes(flag, 4, 32);
        }
        if (payload.Length - offset < xBytes + yBytes) return false;

        ushort[] contours = new ushort[contourCount];
        for (int i = 0; i < contourCount; i++)
            contours[i] = BinaryPrimitives.ReadUInt16BigEndian(payload[(10 + i * 2)..]);
        TerminalGlyphPoint[] points = new TerminalGlyphPoint[pointCount];
        int xOffset = offset, yOffset = offset + xBytes;
        int x = 0, y = 0;
        for (int i = 0; i < pointCount; i++)
        {
            byte flag = flags[i];
            // At most 5,461 signed 16-bit deltas: the accumulated coordinates
            // provably fit in Int32; do not narrow them to header-sized Int16.
            x += ReadDelta(payload, ref xOffset, flag, 2, 16);
            y += ReadDelta(payload, ref yOffset, flag, 4, 32);
            points[i] = new(x, y, (flag & 1) != 0);
        }
        outline = new(contours, points);
        error = TerminalGlyphDecodeError.None;
        return true;
    }

    private static int CoordinateBytes(byte flags, int shortMask, int sameMask) =>
        (flags & shortMask) != 0 ? 1 : (flags & sameMask) != 0 ? 0 : 2;

    private static int ReadDelta(ReadOnlySpan<byte> data, ref int offset, byte flags, int shortMask, int sameMask)
    {
        if ((flags & shortMask) != 0)
        {
            int value = data[offset++];
            return (flags & sameMask) != 0 ? value : -value;
        }
        if ((flags & sameMask) != 0) return 0;
        int delta = BinaryPrimitives.ReadInt16BigEndian(data[offset..]);
        offset += 2;
        return delta;
    }
}
