// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Follow Ghostty PageList.resizeCols' preserved_cursor accounting, including
// its wrap-continuation discount and width/height ordering. WT TextBuffer::Reflow
// maps its fixed buffer cursor and xterm.js Buffer._reflow adjusts ybase/cursor
// (and can exclude the cursor line); neither defines Ghostty's padding policy.
public sealed class ManagedReflowCursorPaddingTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (ushort columns in new ushort[] { 4, 8, 16 })
        foreach (ushort rows in new ushort[] { 2, 4, 6 })
        foreach (bool wrapped in new[] { false, true })
        foreach (bool pull in new[] { false, true })
        foreach (bool restored in new[] { false, true })
            yield return [columns, rows, wrapped, pull, restored];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CursorAndRowsMatchNativeWhenReflowChangesSpaceBelowCursor(
        ushort columns, ushort rows, bool wrapped, bool pull, bool restored)
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1",
                "Required native VT library is unavailable.");
            Assert.Skip("Native VT library is unavailable.");
        }
        byte[] input = Encoding.UTF8.GetBytes("h0\r\nh1\r\nh2\r\nh3\r\nh4\r\nh5\r\n" +
            (wrapped ? "\u001b[Habcdefghijklmno\u001b[2;3H\u001b[J" : "\u001b[2;1H\u001b[J\u001b[2;3H"));
        using GhosttyTerminal native = new(8, 4);
        native.SetResizePullScrollback(pull);
        native.Write(input);
        BasicVtProcessorOptions options = new() { ResizePullScrollback = pull };
        TerminalScreen liveScreen = new(8, 4);
        using BasicVtProcessor? live = restored ? null : new(liveScreen, options);
        using ManagedTerminalSnapshot? snapshot = restored
            ? ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(native), new() { ProcessorOptions = options }) : null;
        BasicVtProcessor processor = snapshot?.Processor ?? live!;
        TerminalScreen screen = snapshot?.Screen ?? liveScreen;
        if (!restored) processor.Process(input);

        native.Resize(columns, rows);
        processor.ResizeScreen(columns, rows, 0, 0);
        AssertNative();
        // Check continuation and returning to the old geometry, not just a
        // blank-row count that could hide a displaced cursor or lost history.
        native.Write("X"u8); processor.Process("X"u8);
        native.Resize(8, 4); processor.ResizeScreen(8, 4, 0, 0);
        AssertNative();

        void AssertNative()
        {
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotScreen reference = reader.ReadReady().Screens[0];
            Assert.Equal((int)reference.State.CursorX, processor.CursorCol);
            Assert.Equal((int)reference.State.CursorY, processor.CursorRow);
            int count = 0;
            foreach (GhosttySnapshotPage page in reference.Pages) count += page.Grid.Rows;
            int index = screen.TotalRows - count;
            Assert.True(index >= 0, $"Managed rows {screen.TotalRows}, native READY rows {count}");
            foreach (GhosttySnapshotPage page in reference.Pages)
            foreach (TerminalRow expected in GhosttySnapshotLivePage.Decode(page, screen))
            {
                TerminalRow actual = screen.GetRow(index++);
                Assert.Equal(expected.WrapsToNext, actual.WrapsToNext);
                Assert.Equal(expected.IsWrapContinuation, actual.IsWrapContinuation);
                for (int column = 0; column < screen.Columns; column++)
                    Assert.Equal(expected.ReadOnlyCells[column].Codepoint, actual.ReadOnlyCells[column].Codepoint);
            }
        }
    }
}
