// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Converts RoyalTerminal's scrollback row limit to the byte budget consumed by
/// the currently pinned libghostty-vt implementation.
/// </summary>
internal static class GhosttyScrollbackBudget
{
    // Page.layout(std_capacity).total_size in the pinned Ghostty layout is
    // 572 KiB, with a 215x215 grid capacity. Both a row and a cell occupy
    // 64 bits, so changing the terminal width divides 215 * (215 cells + one
    // row metadata slot) across rows. These are compatibility assumptions for
    // the current byte-oriented C adapter and must be reviewed whenever the
    // Ghostty pin changes.
    // Ghostty PR #13473 is the future line-oriented replacement:
    // https://github.com/ghostty-org/ghostty/pull/13473
    internal const ulong StandardPageBytes = 572UL * 1024UL;
    internal const ulong StandardPageGridSlots = 215UL * (215UL + 1UL);

    /// <summary>
    /// Calculates the native byte budget required to retain at least the
    /// requested number of scrollback rows at the initial terminal width.
    /// </summary>
    internal static nuint FromRows(int columns, int viewportRows, int scrollbackRows)
    {
        if (scrollbackRows <= 0)
        {
            return 0;
        }

        ulong rowsPerPage = RowsPerPage(columns);
        ulong requiredRows = checked(
            (ulong)scrollbackRows + (ulong)Math.Max(1, viewportRows));
        ulong requiredPages = checked(DivideRoundUp(requiredRows, rowsPerPage) + 1UL);
        ulong nativeMaximum = nuint.Size == sizeof(uint) ? uint.MaxValue : ulong.MaxValue;

        if (requiredPages > nativeMaximum / StandardPageBytes)
        {
            return nuint.MaxValue;
        }

        return (nuint)(requiredPages * StandardPageBytes);
    }

    /// <summary>
    /// Calculates the number of rows stored in a standard Ghostty page for the
    /// specified terminal width.
    /// </summary>
    internal static ulong RowsPerPage(int columns)
    {
        ulong effectiveColumns = (ulong)Math.Max(1, columns);
        return Math.Max(1UL, StandardPageGridSlots / (effectiveColumns + 1UL));
    }

    private static ulong DivideRoundUp(ulong value, ulong divisor)
    {
        return value / divisor + (value % divisor == 0 ? 0UL : 1UL);
    }
}
