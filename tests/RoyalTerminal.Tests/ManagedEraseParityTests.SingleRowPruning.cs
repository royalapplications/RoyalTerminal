// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class ManagedEraseParityTests
{
    // Ghostty Screen.cursorDownScroll rotates/clears in place when its byte
    // limit is zero. xterm.js BufferService.scroll similarly recycles a full
    // zero-history buffer, and WT AdaptDispatch::_DoLineFeed circles its full
    // text buffer. A zero line limit with a positive byte limit is different:
    // Ghostty retains whole pages and repairs the cursor when they are pruned.
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    [InlineData(3, false, false)]
    [InlineData(3, false, true)]
    [InlineData(3, true, false)]
    [InlineData(3, true, true)]
    public void ZeroByteScrollbackRotatesWithoutGrowingHistory(int rows, bool scrollUp, bool held)
    {
        RequireNative();
        const int columns = 215;
        using GhosttyTerminal source = new(columns, (ushort)rows, 0);
        source.Write(System.Text.Encoding.UTF8.GetBytes($"\u001b[{rows};1H\u001b[3;44m\u001b]8;;https://cursor\aX"));
        byte[] snapshot = GhosttySnapshot.Encode(source);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        GhosttySnapshotPageAllocation? page = managed.Screen.GetRow(0).SnapshotAllocation;
        int pageRows = new GhosttySnapshotAllocation(new GhosttySnapshotScrollbackQuota().PageAlignment).InitialRows(columns);
        for (int iteration = 0; iteration < pageRows + 3; iteration++)
        {
            ReadOnlySpan<byte> command = scrollUp ? "\u001b[S\rX"u8 : "\r\nX"u8;
            if (held) managed.Processor.Process("\u001b[?2026h"u8);
            native.Write(command);
            managed.Processor.Process(command);
            if (held) managed.Processor.Process("\u001b[?2026l"u8);
            AssertNative(native, managed, $"zero-byte scroll, rows={rows}, SU={scrollUp}, held={held}, iteration={iteration}");
            Assert.Equal(rows, managed.Screen.TotalRows);
            Assert.Same(page, managed.Screen.GetRow(0).SnapshotAllocation);
        }
        Assert.Equal('X', retained.GetViewportRow(rows - 1).ReadOnlyCells[0].Codepoint);
        Assert.True(retained.TryGetHyperlinkUrl(retained.GetViewportRow(rows - 1).ReadOnlyCells[0].HyperlinkId, out string? uri));
        Assert.Equal("https://cursor", uri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleRowLineFeedPruningPreservesCursorHyperlinkIdentity(bool held)
    {
        RequireNative();
        const int columns = 215;
        using GhosttyTerminal source = new(columns, 1, 1024 * 1024);
        source.SetScrollbackMaxLines(0);
        source.Write("\u001b[3;44m\u001b]8;;https://cursor\aX"u8);
        byte[] snapshot = GhosttySnapshot.Encode(source);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        TerminalScreen retained = managed.Screen.CreateStateCopy();
        int pageRows = new GhosttySnapshotAllocation(new GhosttySnapshotScrollbackQuota().PageAlignment).InitialRows(columns);
        bool pruned = false;
        int previousRows = managed.Screen.TotalRows;
        for (int iteration = 0; iteration < pageRows + 3; iteration++)
        {
            if (held) managed.Processor.Process("\u001b[?2026h"u8);
            native.Write("\r\nX"u8);
            managed.Processor.Process("\r\nX"u8);
            if (held) managed.Processor.Process("\u001b[?2026l"u8);
            pruned |= managed.Screen.TotalRows < previousRows;
            previousRows = managed.Screen.TotalRows;
            AssertNative(native, managed, $"single-row cursor pruning, iteration={iteration}");
        }
        Assert.True(pruned);
        Assert.Equal('X', retained.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.True(retained.TryGetHyperlinkUrl(retained.GetViewportRow(0).ReadOnlyCells[0].HyperlinkId, out string? uri));
        Assert.Equal("https://cursor", uri);
    }

}
