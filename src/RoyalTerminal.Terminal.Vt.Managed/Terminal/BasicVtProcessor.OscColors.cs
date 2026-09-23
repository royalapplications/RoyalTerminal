// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private static bool TryOscColorOperation(ReadOnlySpan<byte> selector, out int operation)
    {
        operation = 0;
        if (selector.Length is < 1 or > 3 || selector[0] == '0') return false;
        foreach (byte digit in selector)
        {
            if (digit is < (byte)'0' or > (byte)'9') return false;
            operation = operation * 10 + digit - '0';
        }
        return operation is 4 or 5 or 21 or 104 or >= 10 and <= 19 or >= 110 and <= 119;
    }

    private void HandleOscColors(int operation, ReadOnlySpan<char> payload, bool bellTerminator)
    {
        bool changed = false;
        StringBuilder? replies = null;
        if (operation == 4)
        {
            while (true)
            {
                ReadOnlySpan<char> indexToken = TakeColorToken(ref payload);
                ReadOnlySpan<char> specification = TakeColorToken(ref payload);
                if (indexToken.IsEmpty || specification.IsEmpty || !TryXtermColorIndex(indexToken, out int index)) break;
                if (specification.SequenceEqual("?"))
                {
                    if (index < 256) AppendOscColorReply(ref replies, 4, index, _colors.GetPalette(index), bellTerminator);
                }
                else
                {
                    // Native retains the valid prefix and stops at the first
                    // malformed pair, including an unsupported target's color.
                    if (!ManagedColorParser.TryParse(specification, out uint color)) break;
                    if (index < 256) { _colors.SetPalette(index, color); changed = true; }
                }
            }
        }
        else if (operation == 104)
        {
            bool hasTarget = false;
            while (true)
            {
                ReadOnlySpan<char> token = TakeColorToken(ref payload);
                if (token.IsEmpty) break;
                if (!TryXtermColorIndex(token, out int index)) continue;
                hasTarget = true;
                if (index < 256) { _colors.ResetPalette(index); changed = true; }
            }
            // No accepted targets means reset all; accepted special targets
            // (256-260) are ignored but suppress the reset-all fallback.
            if (!hasTarget) { _colors.ResetPalette(null); changed = true; }
        }
        else if (operation is >= 110 and <= 112)
        {
            if (TakeColorToken(ref payload).IsEmpty)
            {
                _colors.SetDynamic(operation - 100, null);
                changed = true;
            }
        }
        else if (operation is >= 10 and <= 12)
        {
            for (int selector = operation; selector <= 12; selector++)
            {
                ReadOnlySpan<char> specification = TakeColorToken(ref payload);
                if (specification.IsEmpty) break;
                if (specification.SequenceEqual("?"))
                {
                    uint? color = _colors.GetDynamic(selector) ?? (selector == 12 ? _colors.GetDynamic(10) : null);
                    if (color is uint value) AppendOscColorReply(ref replies, selector, null, value, bellTerminator);
                }
                else
                {
                    if (!ManagedColorParser.TryParse(specification, out uint color)) break;
                    _colors.SetDynamic(selector, color);
                    changed = true;
                }
            }
        }
        // OSC 5 and dynamic targets 13-19/113-119 are recognized by native
        // parsing but have no state/effects in libghostty-vt.
        if (changed) ApplyEffectiveTheme(_colors.GetEffectiveTheme());
        if (replies is not null) ResponseCallback?.Invoke(Encoding.ASCII.GetBytes(replies.ToString()));
    }

    private static ReadOnlySpan<char> TakeColorToken(ref ReadOnlySpan<char> payload)
    {
        while (!payload.IsEmpty)
        {
            int separator = payload.IndexOf(';');
            ReadOnlySpan<char> token = separator < 0 ? payload : payload[..separator];
            payload = separator < 0 ? [] : payload[(separator + 1)..];
            if (!token.IsEmpty) return token;
        }
        return [];
    }

    private static bool TryXtermColorIndex(ReadOnlySpan<char> token, out int index)
    {
        // Zig parseInt(u9) permits +, -0, and interior underscores, but not
        // surrounding whitespace. These rules differ from OSC 21 unsigned keys.
        bool negative = !token.IsEmpty && token[0] == '-';
        if (!token.IsEmpty && token[0] is '+' or '-') token = token[1..];
        return ManagedColorParser.Unsigned(token, 10, 511, out index) && index <= 260 && (!negative || index == 0);
    }

    private void AppendOscColorReply(ref StringBuilder? replies, int selector, int? paletteIndex, uint color, bool bell)
    {
        if (ResponseCallback is null) return;
        replies ??= new();
        replies.Append(CultureInfo.InvariantCulture, $"\u001b]{selector};");
        if (paletteIndex is int index) replies.Append(CultureInfo.InvariantCulture, $"{index};");
        uint r = (color >> 16) & 255, g = (color >> 8) & 255, b = color & 255;
        if (_theme.OscColorReportFormat == TerminalOscColorReportFormat.Bit8)
            replies.Append(CultureInfo.InvariantCulture, $"rgb:{r:x2}/{g:x2}/{b:x2}");
        else replies.Append(CultureInfo.InvariantCulture, $"rgb:{r * 257:x4}/{g * 257:x4}/{b * 257:x4}");
        replies.Append(bell ? "\a" : "\u001b\\");
    }
}
