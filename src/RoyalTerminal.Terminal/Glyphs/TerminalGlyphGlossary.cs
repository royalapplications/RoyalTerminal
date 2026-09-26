// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;

namespace RoyalTerminal.Terminal.Glyphs;

// Session-scoped FIFO storage. Entries are immutable and shared across presentation
// copies; only the small bounded registry/order are copied for synchronized output.
internal sealed class TerminalGlyphGlossary
{
    private const int Capacity = 1024;
    private readonly Dictionary<uint, TerminalGlyphRegistration> _entries = [];
    private readonly List<uint> _order = [];

    internal int Count => _entries.Count;
    internal bool TryGet(uint codepoint, [NotNullWhen(true)] out TerminalGlyphRegistration? entry) => _entries.TryGetValue(codepoint, out entry);
    internal static bool IsPrivateUse(uint codepoint) => codepoint is >= 0xE000 and <= 0xF8FF or >= 0xF0000 and <= 0xFFFFD or >= 0x100000 and <= 0x10FFFD;

    internal void Register(uint codepoint, TerminalGlyphRegistration entry)
    {
        if (!IsPrivateUse(codepoint)) throw new ArgumentOutOfRangeException(nameof(codepoint));
        if (_entries.ContainsKey(codepoint)) _order.Remove(codepoint);
        _entries[codepoint] = entry;
        _order.Add(codepoint);
        if (_order.Count <= Capacity) return;
        _entries.Remove(_order[0]);
        _order.RemoveAt(0);
    }

    internal void Delete(uint codepoint)
    {
        if (_entries.Remove(codepoint)) _order.Remove(codepoint);
    }

    internal void Clear()
    {
        _entries.Clear();
        _order.Clear();
    }

    internal TerminalGlyphGlossary Copy()
    {
        TerminalGlyphGlossary copy = new();
        foreach (uint codepoint in _order) copy.Register(codepoint, _entries[codepoint]);
        return copy;
    }
}
