// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Text;
using System.Text;

namespace RoyalTerminal.Terminal.Glyphs;

internal static class TerminalGlyphResponseFormatter
{
    internal const int MaximumBytes = 128;

    internal static byte[] Status(byte verb, uint codepoint, string? error)
    {
        Span<byte> buffer = stackalloc byte[MaximumBytes];
        return buffer[..WriteStatus(buffer, verb, codepoint, error)].ToArray();
    }

    internal static byte[] Query(uint codepoint, bool glossary, bool system)
    {
        Span<byte> buffer = stackalloc byte[MaximumBytes];
        return buffer[..WriteQuery(buffer, codepoint, glossary, system)].ToArray();
    }

    internal static int WriteStatus(Span<byte> buffer, byte verb, uint codepoint, string? error)
    {
        Writer writer = new(buffer);
        writer.Bytes("\u001b_25a1;"u8);
        writer.Byte(verb);
        if (verb == (byte)'r') { writer.Bytes(";cp="u8); writer.Hex(codepoint); }
        writer.Bytes(";status="u8);
        writer.Byte(error is null ? (byte)'0' : (byte)'1');
        if (error is not null)
        {
            writer.Bytes(";reason="u8);
            writer.Ascii(error);
        }
        writer.Bytes("\u001b\\"u8);
        return writer.Count;
    }

    internal static int WriteQuery(Span<byte> buffer, uint codepoint, bool glossary, bool system)
    {
        Writer writer = new(buffer);
        writer.Bytes("\u001b_25a1;q;cp="u8);
        writer.Hex(codepoint);
        writer.Bytes(";status="u8);
        if (system) writer.Bytes("system"u8);
        if (system && glossary) writer.Byte((byte)',');
        if (glossary) writer.Bytes("glossary"u8);
        writer.Bytes("\u001b\\"u8);
        return writer.Count;
    }

    private ref struct Writer
    {
        private readonly Span<byte> _buffer;
        internal int Count { get; private set; }
        internal Writer(Span<byte> buffer)
        {
            if (buffer.Length < MaximumBytes) throw new ArgumentException("Glyph reply scratch is too small.", nameof(buffer));
            _buffer = buffer;
            Count = 0;
        }
        internal void Byte(byte value) => _buffer[Count++] = value;
        internal void Bytes(ReadOnlySpan<byte> bytes) { bytes.CopyTo(_buffer[Count..]); Count += bytes.Length; }
        internal void Ascii(string value) => Count += Encoding.ASCII.GetBytes(value, _buffer[Count..]);
        internal void Hex(uint value)
        {
            if (!Utf8Formatter.TryFormat(value, _buffer[Count..], out int written, 'x'))
                throw new InvalidOperationException("Bounded glyph codepoint did not fit.");
            Count += written;
        }
    }
}
