// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

// Ghostty dcs.Command.DECRQSS uses bounded bytes, not intermediate strings.
// Replies deliberately differ from replay SGR: native omits underline color
// and emits the initial reset, compact ANSI colors and colon RGB parameters.
internal static class ManagedStatusReplyFormatter
{
    internal const int MaximumBytes = 256;

    internal static int Sgr(Span<byte> destination, in TerminalCell pen)
    {
        Writer writer = new(destination);
        writer.Append("\u001bP1$r0"u8);
        CellAttributes attributes = pen.Attributes;
        if ((attributes & CellAttributes.Bold) != 0) writer.Append(";1"u8);
        if ((attributes & CellAttributes.Dim) != 0) writer.Append(";2"u8);
        if ((attributes & CellAttributes.Italic) != 0) writer.Append(";3"u8);
        TerminalUnderlineStyle underline = pen.UnderlineStyle;
        if (underline == TerminalUnderlineStyle.None && (attributes & CellAttributes.Underline) != 0)
            underline = TerminalUnderlineStyle.Single;
        if (underline != TerminalUnderlineStyle.None)
        {
            writer.Append(";4"u8);
            if (underline != TerminalUnderlineStyle.Single)
            {
                writer.Byte((byte)':');
                writer.Number((int)underline);
            }
        }
        if ((pen.Decorations & CellDecorations.Overline) != 0) writer.Append(";53"u8);
        if ((attributes & CellAttributes.Blink) != 0) writer.Append(";5"u8);
        if ((attributes & CellAttributes.Inverse) != 0) writer.Append(";7"u8);
        if ((attributes & CellAttributes.Hidden) != 0) writer.Append(";8"u8);
        if ((attributes & CellAttributes.Strikethrough) != 0) writer.Append(";9"u8);
        Color(ref writer, 30, pen.ForegroundIdentity);
        Color(ref writer, 40, pen.BackgroundIdentity);
        writer.Append("m\u001b\\"u8);
        return writer.Length;
    }

    internal static int Margins(Span<byte> destination, int first, int last, bool horizontal)
    {
        Writer writer = new(destination);
        writer.Append("\u001bP1$r"u8);
        writer.Number(first); writer.Byte((byte)';'); writer.Number(last);
        writer.Byte(horizontal ? (byte)'s' : (byte)'r');
        writer.Append("\u001b\\"u8);
        return writer.Length;
    }

    internal static int Cursor(Span<byte> destination, int style)
    {
        Writer writer = new(destination);
        writer.Append("\u001bP1$r"u8); writer.Number(style); writer.Append(" q\u001b\\"u8);
        return writer.Length;
    }

    internal static int Unsupported(Span<byte> destination)
    {
        Writer writer = new(destination);
        writer.Append("\u001bP0$r\u001b\\"u8);
        return writer.Length;
    }

    private static void Color(ref Writer writer, int basis, TerminalColorIdentity color)
    {
        if (color.Kind == TerminalColorKind.Default) return;
        writer.Byte((byte)';');
        int value = (int)color.Value;
        if (color.Kind == TerminalColorKind.Palette)
        {
            if (value < 16) writer.Number(basis + (value < 8 ? value : value + 52));
            else { writer.Number(basis + 8); writer.Append(":5:"u8); writer.Number(value); }
            return;
        }
        writer.Number(basis + 8); writer.Append(":2::"u8);
        writer.Number((value >> 16) & 255); writer.Byte((byte)':');
        writer.Number((value >> 8) & 255); writer.Byte((byte)':');
        writer.Number(value & 255);
    }

    private ref struct Writer
    {
        private readonly Span<byte> _destination;
        internal int Length { get; private set; }

        internal Writer(Span<byte> destination)
        {
            if (destination.Length < MaximumBytes) throw new ArgumentException("Status reply buffer is too small.", nameof(destination));
            _destination = destination;
            Length = 0;
        }

        internal void Append(ReadOnlySpan<byte> bytes)
        {
            bytes.CopyTo(_destination[Length..]);
            Length += bytes.Length;
        }

        internal void Byte(byte value) => _destination[Length++] = value;

        internal void Number(int value)
        {
            if (!Utf8Formatter.TryFormat(value, _destination[Length..], out int written))
                throw new InvalidOperationException("Status reply exceeds its bounded buffer.");
            Length += written;
        }
    }
}
