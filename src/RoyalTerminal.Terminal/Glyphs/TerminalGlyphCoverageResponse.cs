// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Text;
using System.Text;

namespace RoyalTerminal.Terminal.Glyphs;

internal static class TerminalGlyphCoverageResponse
{
    internal static byte[] Format(uint codepoint, bool glossary, ITerminalGlyphCoverageSource? source)
        => Format(codepoint, glossary, HasSystemGlyph(source, codepoint));

    private static byte[] Format(uint codepoint, bool glossary, bool system)
    {
        string status = (system, glossary) switch
        {
            (true, true) => "system,glossary",
            (true, false) => "system",
            (false, true) => "glossary",
            _ => string.Empty,
        };
        return Encoding.ASCII.GetBytes($"\u001b_25a1;q;cp={codepoint:x};status={status}\u001b\\");
    }

    // Only rewrite a complete, canonical query reply emitted by our native
    // library. Other PTY traffic, unknown statuses and framing pass unchanged.
    internal static byte[]? Augment(ReadOnlySpan<byte> reply, ITerminalGlyphCoverageSource? source)
    {
        ReadOnlySpan<byte> prefix = "\u001b_25a1;q;cp="u8;
        if (source is null || reply.Length > 64 || !reply.StartsWith(prefix) || !reply.EndsWith("\u001b\\"u8)) return null;
        ReadOnlySpan<byte> payload = reply[prefix.Length..^2];
        int delimiter = payload.IndexOf(";status="u8);
        if (delimiter < 1 || !Utf8Parser.TryParse(payload[..delimiter], out uint codepoint, out int consumed, 'X') || consumed != delimiter)
            return null;
        ReadOnlySpan<byte> status = payload[(delimiter + 8)..];
        bool glossary = status.SequenceEqual("glossary"u8);
        if ((!status.IsEmpty && !glossary) || !HasSystemGlyph(source, codepoint)) return null;
        return Format(codepoint, glossary, system: true);
    }

    private static bool HasSystemGlyph(ITerminalGlyphCoverageSource? source, uint codepoint)
    {
        if (source is null || codepoint > 0x10FFFF || codepoint is >= 0xD800 and <= 0xDFFF) return false;
        try { return source.HasSystemGlyph(codepoint); }
        catch { return false; } // Host/resource failure must preserve the protocol reply.
    }
}
