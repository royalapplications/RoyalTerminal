// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Ghostty's snapshot-v1 mode registry and safe embedder policy subset.</summary>
internal static class TerminalModeRegistry
{
    internal static ReadOnlySpan<int> AnsiModes => [2, 4, 12, 20];
    internal static ReadOnlySpan<int> DecModes =>
    [
        1, 3, 4, 5, 6, 7, 8, 9, 12, 25, 40, 45, 47, 66, 67, 69,
        1000, 1002, 1003, 1004, 1005, 1006, 1007, 1015, 1016, 1035,
        1036, 1039, 1045, 1047, 1048, 1049, 2004, 2026, 2027, 2031,
        2033, 2048, 5522,
    ];

    internal const ulong InitialValues = (1UL << 2) | (1UL << 9) |
        (1UL << 13) | (1UL << 26) | (1UL << 29) | (1UL << 30);

    internal static int IndexOf(int mode, bool ansi)
    {
        int index = ansi ? AnsiModes.IndexOf(mode) : DecModes.IndexOf(mode);
        return index < 0 || ansi ? index : index + 4;
    }

    // Ghostty modes.zig default_configurable: transition/derived-state modes
    // must go through their semantic APIs instead of changing reset policy.
    internal static bool IsDefaultConfigurable(int mode, bool ansi) => ansi
        ? mode is 2 or 4 or 12 or 20
        : mode is 1 or 4 or 5 or 7 or 8 or 25 or 40 or 45 or 66 or 67 or
            1004 or 1007 or 1035 or 1036 or 1039 or 1045 or 2004 or 2027 or
            2031 or 2048 or 5522;
}
