// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

internal static class TerminalFontCoverage
{
    internal static unsafe bool IsLastResort(SKTypeface typeface)
    {
        // OpenType head.flags bit 14 denotes generic range placeholders, not
        // real coverage. Ghostty also rejects Apple's LastResort by name.
        byte* flags = stackalloc byte[2];
        flags[0] = flags[1] = 0;
        if (typeface.TryGetTableData(0x68656164, 16, 2, (nint)flags) && (flags[0] & 0x40) != 0)
            return true;
        return typeface.FamilyName is "LastResort" or ".LastResort";
    }
}
