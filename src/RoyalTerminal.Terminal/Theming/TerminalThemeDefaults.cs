// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Theming;

internal static class TerminalThemeDefaults
{
    // Canonical xterm base ANSI colors (0-15), ARGB.
    internal static readonly uint[] Base16Canonical =
    [
        0xFF000000, // 0 black
        0xFFCD0000, // 1 red
        0xFF00CD00, // 2 green
        0xFFCDCD00, // 3 yellow
        0xFF0000EE, // 4 blue
        0xFFCD00CD, // 5 magenta
        0xFF00CDCD, // 6 cyan
        0xFFE5E5E5, // 7 white
        0xFF7F7F7F, // 8 bright black
        0xFFFF0000, // 9 bright red
        0xFF00FF00, // 10 bright green
        0xFFFFFF00, // 11 bright yellow
        0xFF5C5CFF, // 12 bright blue
        0xFFFF00FF, // 13 bright magenta
        0xFF00FFFF, // 14 bright cyan
        0xFFFFFFFF, // 15 bright white
    ];

    internal const uint DefaultDarkForeground = 0xFFD4D4D4;
    internal const uint DefaultDarkBackground = 0xFF1E1E1E;
    internal const uint DefaultLightForeground = 0xFF111111;
    internal const uint DefaultLightBackground = 0xFFFFFFFF;
}
