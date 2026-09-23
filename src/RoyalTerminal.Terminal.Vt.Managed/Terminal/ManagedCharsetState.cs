// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.Terminal;

// Same compact registry as Ghostty snapshot CharsetState: four two-bit sets,
// GL/GR slots, and an optional single shift. No tables or per-terminal arrays.
internal struct ManagedCharsetState
{
    private ushort _bits;

    public ManagedCharsetState() => _bits = 2 << 10; // UTF-8 sets; GL=G0, GR=G2.
    internal readonly ushort Bits => _bits;

    internal void Designate(int slot, byte final)
    {
        int set = final switch { (byte)'B' => 1, (byte)'A' => 2, (byte)'0' => 3, _ => -1 };
        if (set < 0 || (uint)slot > 3) return;
        int shift = slot * 2;
        _bits = (ushort)((_bits & ~(3 << shift)) | (set << shift));
    }

    internal void Invoke(int slot, bool right = false, bool single = false)
    {
        int shift = single ? 12 : right ? 10 : 8;
        int mask = single ? 7 : 3;
        int value = single ? slot + 1 : slot;
        _bits = (ushort)((_bits & ~(mask << shift)) | (value << shift));
    }

    internal int MapPrintedCell(int codepoint)
    {
        int slot = (_bits >> 12) & 7;
        if (slot != 0)
        {
            slot--;
            _bits &= 0x0FFF;
        }
        else slot = (_bits >> 8) & 3;
        int set = (_bits >> (slot * 2)) & 3;
        // Current Ghostty maps through GL even for UTF-8 input; GR is retained
        // state but not yet consulted by its printer. Width is determined first.
        if (set <= 1) return codepoint;
        if (codepoint > 255) return ' ';
        if (set == 2) return codepoint == '#' ? 0xA3 : codepoint;
        return codepoint switch
        {
            '`' => 0x25C6, 'a' => 0x2592, 'b' => 0x2409, 'c' => 0x240C,
            'd' => 0x240D, 'e' => 0x240A, 'f' => 0xB0, 'g' => 0xB1,
            'h' => 0x2424, 'i' => 0x240B, 'j' => 0x2518, 'k' => 0x2510,
            'l' => 0x250C, 'm' => 0x2514, 'n' => 0x253C, 'o' => 0x23BA,
            'p' => 0x23BB, 'q' => 0x2500, 'r' => 0x23BC, 's' => 0x23BD,
            't' => 0x251C, 'u' => 0x2524, 'v' => 0x2534, 'w' => 0x252C,
            'x' => 0x2502, 'y' => 0x2264, 'z' => 0x2265, '{' => 0x3C0,
            '|' => 0x2260, '}' => 0xA3, '~' => 0xB7,
            _ => codepoint,
        };
    }

    internal readonly void AppendRestoreSequence(StringBuilder builder)
    {
        for (int slot = 0; slot < 4; slot++)
        {
            int set = (_bits >> (slot * 2)) & 3;
            builder.Append('\u001b').Append("()*+"[slot]).Append(set switch { 2 => 'A', 3 => '0', _ => 'B' });
        }
        builder.Append(((_bits >> 8) & 3) switch { 1 => "\u000e", 2 => "\u001bn", 3 => "\u001bo", _ => "\u000f" });
        // No escape exists for GR=G0; normal input can only select G1/G2/G3.
        builder.Append(((_bits >> 10) & 3) switch { 1 => "\u001b~", 3 => "\u001b|", _ => "\u001b}" });
        int single = (_bits >> 12) & 7;
        if (single == 3) builder.Append("\u001bN");
        else if (single == 4) builder.Append("\u001bO");
    }
}
