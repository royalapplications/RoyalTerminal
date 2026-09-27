// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.Globalization;

namespace RoyalTerminal.Terminal;

/// <summary>A configured inclusive codepoint range and its requested font family.</summary>
/// <param name="First">First codepoint in the range.</param>
/// <param name="Last">Last codepoint in the range.</param>
/// <param name="FamilyName">Requested family; later mappings take precedence.</param>
public readonly record struct TerminalCodepointFontMapping(int First, int Last, string FamilyName);

/// <summary>Parses Ghostty-style U+XXXX[-U+YYYY][,...]=family configuration entries.</summary>
public static class TerminalFontCodepointMap
{
    /// <summary>
    /// Parses all entries atomically, preserving order and overlapping ranges.
    /// Values up to Ghostty's 21-bit configuration limit are accepted; non-scalars
    /// never match rendered Unicode text. Empty lines and empty range lists do nothing.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<string> entries,
        out ImmutableArray<TerminalCodepointFontMapping> mappings, out string? error)
        => TryParseCore(entries, retainMappings: true, out mappings, out error);

    /// <summary>Validates the complete mapping grammar without allocating parsed ranges or family strings.</summary>
    public static bool TryValidate(ReadOnlySpan<string> entries, out string? error)
        => TryParseCore(entries, retainMappings: false, out _, out error);

    private static bool TryParseCore(ReadOnlySpan<string> entries, bool retainMappings,
        out ImmutableArray<TerminalCodepointFontMapping> mappings, out string? error)
    {
        if (entries.IsEmpty) { mappings = []; error = null; return true; }
        ImmutableArray<TerminalCodepointFontMapping>.Builder? result = retainMappings
            ? ImmutableArray.CreateBuilder<TerminalCodepointFontMapping>() : null;
        for (int line = 0; line < entries.Length; line++)
        {
            ReadOnlySpan<char> entry = entries[line].AsSpan().Trim();
            if (entry.IsEmpty) continue;
            int separator = entry.IndexOf('=');
            if (separator < 0) return Fail(line, out mappings, out error);
            ReadOnlySpan<char> ranges = entry[..separator].Trim();
            string? family = retainMappings ? entry[(separator + 1)..].Trim().ToString() : null;
            while (!ranges.IsEmpty)
            {
                int comma = ranges.IndexOf(',');
                ReadOnlySpan<char> range = (comma < 0 ? ranges : ranges[..comma]).Trim();
                int hyphen = range.IndexOf('-');
                ReadOnlySpan<char> firstText = hyphen < 0 ? range : range[..hyphen].Trim();
                if (!TryCodepoint(firstText, out int first)) return Fail(line, out mappings, out error);
                int last = first;
                if (hyphen >= 0 && !TryCodepoint(range[(hyphen + 1)..].Trim(), out last)) return Fail(line, out mappings, out error);
                if (last < first) return Fail(line, out mappings, out error);
                result?.Add(new(first, last, family!));
                if (comma < 0) break;
                ranges = ranges[(comma + 1)..].Trim();
                // Ghostty accepts a trailing comma after a range, but not
                // after a single codepoint. Keep its configuration grammar.
                if (ranges.IsEmpty && hyphen < 0) return Fail(line, out mappings, out error);
            }
        }
        mappings = result?.ToImmutable() ?? [];
        error = null;
        return true;

        static bool Fail(int line, out ImmutableArray<TerminalCodepointFontMapping> mappings, out string? error)
        {
            mappings = [];
            error = $"Invalid codepoint mapping on line {line + 1}. Use U+XXXX[-U+YYYY][,...]=font family.";
            return false;
        }
    }

    private static bool TryCodepoint(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        return text.StartsWith("U+", StringComparison.Ordinal) && text.Length > 2 &&
            int.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value) &&
            (uint)value <= 0x1FFFFF;
    }
}
