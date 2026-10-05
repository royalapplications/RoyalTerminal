// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>
/// Builds unpublished live cell storage for a validated READY state. Processor
/// state, continuation and incremental history must be installed by the adapter
/// before publication; this is deliberately not a public restore API.
/// </summary>
internal static class GhosttySnapshotLiveScreen
{
    internal static TerminalScreen Stage(GhosttySnapshotReadyState ready, TerminalTheme hostTheme, int scrollbackLimit)
    {
        ArgumentNullException.ThrowIfNull(ready);
        ArgumentNullException.ThrowIfNull(hostTheme);
        ArgumentOutOfRangeException.ThrowIfNegative(scrollbackLimit);
        GhosttySnapshotTerminalHeader header = ready.Terminal.Header;
        TerminalTheme theme = ResolveTheme(ready.Terminal, hostTheme);
        TerminalScreen result = TerminalScreen.CreateSnapshotStorage(header.Columns, header.Rows, scrollbackLimit, theme);
        TerminalRow[]? primary = null, alternate = null;
        foreach (GhosttySnapshotScreen screen in ready.Screens)
        {
            int count = 0;
            foreach (GhosttySnapshotPage page in screen.Pages) count = checked(count + page.Grid.Rows);
            TerminalRow[] rows = new TerminalRow[count];
            int offset = 0;
            foreach (GhosttySnapshotPage page in screen.Pages)
            {
                TerminalRow[] decoded = GhosttySnapshotLivePage.Decode(page, result);
                decoded.CopyTo(rows, offset);
                offset += decoded.Length;
            }
            if (screen.State.Key == 0) primary = rows;
            else alternate = rows;
        }
        result.InstallSnapshotRows(primary ?? throw new InvalidDataException("Missing primary snapshot screen."),
            alternate, header.ActiveScreenKey);
        return result;
    }

    private static TerminalTheme ResolveTheme(GhosttySnapshotTerminalState state, TerminalTheme host)
    {
        uint[] colors = new uint[256];
        List<int> overrides = [];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = 0xFF000000 | state.CurrentPaletteColor(i);
            if (state.HasPaletteOverride(i)) overrides.Add(i);
        }
        GhosttySnapshotTerminalHeader header = state.Header;
        return new(Resolve(header.Foreground, host.DefaultForeground), Resolve(header.Background, host.DefaultBackground),
            Resolve(header.CursorColor, host.CursorColor), new TerminalPalette(colors, overrides),
            host.PaletteGenerationMode, host.OscColorReportFormat, host.SelectionForeground, host.SelectionBackground,
            host.BoldColor, host.CursorTextColor);
    }

    private static uint Resolve(GhosttySnapshotDynamicColor color, uint fallback)
        => (color.Override ?? color.Default) is { } value ? 0xFF000000 | value : fallback;
}
