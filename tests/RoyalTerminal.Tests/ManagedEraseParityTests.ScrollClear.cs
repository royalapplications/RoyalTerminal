// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// ED22 is a Ghostty/Kitty extension, not ordinary ED2: Terminal.eraseDisplay
// calls Screen.scrollClear even on the alternate screen. PageList.grow may
// retain incidental page-local history despite the alternate screen having no
// user scrollback. WT and xterm.js ED dispatchers only support modes 0..3, so
// their alternate ED2 clearing is not a reference for this extension.
public sealed partial class ManagedEraseParityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AlternateScrollClearRecyclesWholePagesAndRestoresTheirHistory(bool restored, bool held)
    {
        RequireNative();
        const int columns = 215;
        byte[] setup = Encoding.UTF8.GetBytes("primary\u001b[?47h\u001b[Hfirst\r\nsecond\r\nthird\u001b[1;4H");
        using GhosttyTerminal source = new(columns, 3, 1024 * 1024);
        source.Write(setup);
        byte[] snapshot = GhosttySnapshot.Encode(source);
        using GhosttyTerminal native = restored ? GhosttySnapshot.Decode(snapshot) : new(columns, 3, 1024 * 1024);
        TerminalScreen liveScreen = new(columns, 3);
        using ManagedTerminalSnapshot managed = restored ? ManagedTerminalSnapshot.Restore(snapshot)
            : new(liveScreen, new BasicVtProcessor(liveScreen), 0, 0);
        if (!restored)
        {
            native.Write(setup);
            managed.Processor.Process(setup);
        }
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        GhosttySnapshotAllocation layout = new(new GhosttySnapshotScrollbackQuota().PageAlignment);
        int iterations = layout.InitialRows(columns) + 3;
        bool recycled = false;
        nuint previousRows = native.GetTotalRows();
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            ReadOnlySpan<byte> command = "\u001b[3;1HQ\u001b[1;4H\u001b[22J"u8;
            if (held) managed.Processor.Process("\u001b[?2026h"u8);
            native.Write(command);
            managed.Processor.Process(command);
            if (held) managed.Processor.Process("\u001b[?2026l"u8);
            nuint count = native.GetTotalRows();
            recycled |= count < previousRows;
            previousRows = count;
            Assert.True(count == (nuint)managed.Screen.TotalRows,
                $"Iteration {iteration}, restored={restored}: native rows={count}, managed rows={managed.Screen.TotalRows}; " +
                $"first capacity={managed.Screen.GetRow(0).SnapshotAllocation?.Capacity}, " +
                $"tail capacity={managed.Screen.GetRow(managed.Screen.TotalRows - 1).SnapshotAllocation?.Capacity}");
            AssertNative(native, managed, $"alternate iteration {iteration}, restored={restored}, held={held}");
        }
        Assert.True(recycled);
        AssertNative(native, managed, $"alternate page recycling, restored={restored}");
        Assert.Equal(0, managed.Screen.MaxScrollOffset);
        Assert.Equal(3, retained.TotalRows);
        Assert.Equal('f', retained.GetViewportRow(0).ReadOnlyCells[0].Codepoint);

        // Complete historical alternate pages are accepted by the native
        // decoder within its effective byte floor, even though its scrollbar
        // remains disabled. Compare one-shot public restore, not just READY.
        byte[] exported = GhosttySnapshot.Encode(native);
        using GhosttyTerminal nativeCopy = GhosttySnapshot.Decode(exported);
        using ManagedTerminalSnapshot managedCopy = ManagedTerminalSnapshot.Restore(exported);
        AssertNative(nativeCopy, managedCopy, "restored alternate history");
        Assert.True(managedCopy.Screen.TotalRows > managedCopy.Screen.ViewportRows);
        Assert.Equal(0, managedCopy.Screen.MaxScrollOffset);
        nativeCopy.Write("Z"u8);
        managedCopy.Processor.Process("Z"u8);
        AssertNative(nativeCopy, managedCopy, "write after alternate history restore");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PruningTheAlternateCursorPagePreservesItsOwnedStyleAndHyperlink(bool held)
    {
        RequireNative();
        const int columns = 215;
        using GhosttyTerminal source = new(columns, 3, 1024 * 1024);
        source.Write("\u001b[?47hfirst\r\nsecond\r\nthird\u001b[1;4H\u001b[3;44m\u001b]8;;https://cursor\a"u8);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(source));
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        GhosttySnapshotAllocation layout = new(new GhosttySnapshotScrollbackQuota().PageAlignment);
        int pageRows = layout.InitialRows(columns);
        int previousRows = managed.Screen.TotalRows;
        bool prunedCursorPage = false;
        for (int iteration = 0; iteration < pageRows + 3; iteration++)
        {
            if (held) managed.Processor.Process("\u001b[?2026h"u8);
            managed.Processor.Process("\u001b[3;1HQ\u001b[1;4H\u001b[22J"u8);
            if (held) managed.Processor.Process("\u001b[?2026l"u8);
            int count = managed.Screen.TotalRows;
            prunedCursorPage = previousRows > pageRows && count < 10;
            if (prunedCursorPage) break;
            previousRows = count;
        }
        Assert.True(prunedCursorPage);

        // Intentional safety divergence: PageList.erasePage remaps a native
        // cursor pin before Screen.cursorReload sees it. On the pinned native
        // revision this can skip style/link migration and leave stale page IDs;
        // ReleaseFast loses links on subsequent writes. Managed ownership must
        // reacquire both references, including a fresh implicit OSC8 identity,
        // rather than reproducing that loss. Ordinary ED22 cursor migrations
        // are still compared exactly against native in the other tests.
        managed.Processor.Process("Z"u8);
        TerminalCell cell = managed.Screen.GetViewportRow(0).ReadOnlyCells[0];
        Assert.Equal('Z', cell.Codepoint);
        Assert.Equal(CellAttributes.Italic, cell.Attributes & CellAttributes.Italic);
        Assert.True(managed.Screen.TryGetHyperlinkUrl(cell.HyperlinkId, out string? uri));
        Assert.Equal("https://cursor", uri);
        Assert.Equal(3, retained.TotalRows);
        Assert.Equal('f', retained.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("X", false)]
    [InlineData("X", true)]
    [InlineData("X\r\n\u001b[44m\u001b[2K\u001b[0m", false)]
    [InlineData("X\r\n\u001b[44m\u001b[2K\u001b[0m", true)]
    [InlineData("\u001b]133;A\a", false)]
    [InlineData("\u001b]133;A\a", true)]
    [InlineData("one\r\ntwo\r\nthree", false)]
    [InlineData("one\r\ntwo\r\nthree", true)]
    public void AlternateScrollClearMatchesNativeAndKeepsPrimaryIsolated(string contents, bool held)
    {
        RequireNative();
        using GhosttyTerminal source = new(8, 3, 1024 * 1024);
        source.Write(Encoding.UTF8.GetBytes("primary\u001b[?47h" + contents +
            "\u001b[3;5H\u001b[3;45m\u001b]8;;https://cursor\a"));
        byte[] snapshot = GhosttySnapshot.Encode(source);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        TerminalCell[] retainedCells = retained.GetViewportRow(0).ReadOnlyCells.ToArray();
        if (held) managed.Processor.Process("\u001b[?2026h"u8);
        native.Write("\u001b[22J"u8);
        managed.Processor.Process("\u001b[22J"u8);
        if (held)
        {
            Assert.Equal(retainedCells, managed.Screen.GetViewportRow(0).ReadOnlyCells.ToArray());
            managed.Processor.Process("\u001b[?2026l"u8);
        }
        AssertNative(native, managed, "alternate ED22 " + Convert.ToHexString(Encoding.UTF8.GetBytes(contents)));
        Assert.Equal(0, managed.Screen.MaxScrollOffset);
        managed.Screen.ScrollOffset = 1;
        Assert.Equal(0, managed.Screen.ScrollOffset);
        Assert.Equal(retainedCells, retained.GetViewportRow(0).ReadOnlyCells.ToArray());
        byte[][] follow = [Encoding.UTF8.GetBytes("Z\u0301" + Close), "\u001b[?47l"u8.ToArray(), "\u001b[?47h"u8.ToArray()];
        foreach (byte[] command in follow)
        {
            native.Write(command);
            managed.Processor.Process(command);
            AssertNative(native, managed, "alternate ED22 continuation " + Convert.ToHexString(command));
        }
    }
}
