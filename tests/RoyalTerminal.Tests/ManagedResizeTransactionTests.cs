// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Screen.resize promises unchanged state on error and uses allocation
// tripwires. WT TextBuffer::Reflow writes to a destination buffer; xterm.js
// Buffer.resize supplies layout/cursor behavior, not an allocation-failure
// contract. Follow Ghostty, extended to one host transaction for both buffers,
// caller-owned selection anchors and pending synchronized-output publication.
public sealed class ManagedResizeTransactionTests
{
    public static IEnumerable<object[]> Failures()
    {
        for (int checkpoint = 0; checkpoint <= (int)ManagedResizeCheckpoint.Ready; checkpoint++)
            foreach (bool alternate in new[] { false, true })
                foreach (bool hold in new[] { false, true })
                    foreach (bool reflow in new[] { false, true })
                        yield return [checkpoint, alternate, hold, reflow];
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public void EveryBoundaryRollsBackBothBuffersAndCanRetry(int checkpoint, bool alternate, bool hold, bool reflow)
    {
        ManualClock clock = new();
        TerminalScreen screen = new(12, 4, 20) { SnapshotScrollbackQuota = new() { PageAlignment = 4096 } };
        using BasicVtProcessor processor = new(screen, new() { TimeProvider = clock });
        processor.NotifyResize(12, 4, 120, 80);
        // Independent saved/live cursors, SGR, graphemes, prompt metadata,
        // non-default margins and custom tab stops are covered by binary equality.
        Write(processor, "\u001b[1m\u001b]8;;primary\u001b\\ABCDEFGHIJKL\u001b7\r\nM\u0301\u001b[?47h" +
            "\u001b[H\u001b[3m\u001b]8;;alternate\u001b\\ALT\u001b7\u001b[3;8H");
        if (!alternate) Write(processor, "\u001b[?47l");
        Write(processor, "\u001b]133;A;redraw=last\a\u001b]8;;active\u001b\\P" +
            "\u001b[3g\u001b[4G\u001bH\u001b[2;4r\u001b[?69h\u001b[2;10s\u001b[?2048h");
        if (hold) Write(processor, "\u001b[?2026hHIDDEN");
        byte[] before = processor.GetBinarySnapshot();
        TerminalRow published = screen.GetViewportRow(0);
        object cells = published.SearchStorageIdentity;
        published.IsDirty = false;
        TerminalScreenAnchor anchor = screen.CreateAnchor(0, 1)!;
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition anchorBefore));
        TerminalGridPosition[] positions = [new(2, 0), new(9, 2)];
        TerminalGridPosition[] originalPositions = (TerminalGridPosition[])positions.Clone();
        TimeSpan? delay = processor.NextTimedRefreshDelay;
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        OutOfMemoryException failure = new("Injected resize allocation failure");
        processor.ResizeCheckpoint = phase => { if ((int)phase == checkpoint) throw failure; };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() =>
            processor.ResizeScreen(6, 3, 90, 90, reflow, positions)));

        Assert.Equal(before, processor.GetBinarySnapshot());
        Assert.Equal(originalPositions, positions);
        Assert.Equal(12, screen.Columns);
        Assert.Equal(4, screen.ViewportRows);
        Assert.Same(published, screen.GetViewportRow(0));
        Assert.Same(cells, published.SearchStorageIdentity);
        Assert.False(published.IsDirty);
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition anchorAfter));
        Assert.Equal(anchorBefore, anchorAfter);
        Assert.Equal(delay, processor.NextTimedRefreshDelay);
        Assert.Empty(replies);

        processor.ResizeCheckpoint = null;
        processor.ResizeScreen(6, 3, 90, 90, reflow, positions);
        Assert.Equal(6, screen.Columns);
        Assert.Equal(3, screen.ViewportRows);
        Assert.Equal(alternate, screen.AlternateBufferActive);
        Assert.Equal("\u001b[48;3;6;90;90t", Assert.Single(replies));
        Assert.Null(processor.NextTimedRefreshDelay);
        Write(processor, "\u001b8X"); // Both cursor registers remain usable.
        Write(processor, alternate ? "\u001b[?47l\u001b8Y" : "\u001b[?47h\u001b8Y");
        Assert.Equal(6, screen.Columns);
        Assert.Equal(3, screen.ViewportRows);
    }

    [Fact]
    public void FailureRestoresPenRegistersAfterAStagedStyleChange()
    {
        TerminalScreen screen = new(8, 2) { SnapshotScrollbackQuota = new() };
        using BasicVtProcessor processor = new(screen);
        Write(processor, "\u001b[1;3;4:3;9;53;38;5;123;48;2;17;34;51;58;2;68;85;102mA");
        byte[] before = processor.GetBinarySnapshot();
        TerminalCell expected = screen.GetViewportRow(0).ReadOnlyCells[0];
        // Exercise the pen side of rollback independently of where physical
        // allocation fails: resize may degrade a restored pen before a later
        // fallible stage. Mutation is confined to the staged processor state.
        processor.ResizeCheckpoint = phase =>
        {
            if (phase != ManagedResizeCheckpoint.Ready) return;
            Write(processor, "\u001b[0m");
            throw new OutOfMemoryException("After staged pen degradation");
        };
        Assert.Throws<OutOfMemoryException>(() => processor.ResizeScreen(8, 2, 80, 40, false));
        processor.ResizeCheckpoint = null;
        Assert.Equal(before, processor.GetBinarySnapshot());
        Write(processor, "A");
        Assert.Equal(expected, screen.GetViewportRow(0).ReadOnlyCells[1]);
    }

    [Fact]
    public void FailurePreservesMouseMotionSuppressionAndSuccessResetsIt()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        Write(processor, "\u001b[?1003;1006h");
        TerminalPointerEvent pointer = new(Kind: TerminalPointerEventKind.Move, X: 11, Y: 12,
            Button: TerminalMouseButton.None, Action: TerminalInputAction.Press, Modifiers: TerminalModifiers.None);
        TerminalPointerEncodingContext context = new(ScreenWidthPx: 80, ScreenHeightPx: 40, CellWidthPx: 10, CellHeightPx: 20);
        Assert.True(processor.TryEncodePointer(pointer, context, out _));
        FailAt(processor, ManagedResizeCheckpoint.Ready);
        Assert.Throws<OutOfMemoryException>(() => processor.ResizeScreen(8, 2, 80, 40, true));
        Assert.False(processor.TryEncodePointer(pointer, context, out _));
        processor.ResizeCheckpoint = null;
        processor.ResizeScreen(8, 2, 80, 40, true);
        Assert.True(processor.TryEncodePointer(pointer, context, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotifyResizeRollsBackOnlyItsOwnedState(bool hold)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        processor.NotifyResize(8, 2, 80, 40);
        Write(processor, "A\u001b[?2048h");
        if (hold) Write(processor, "\u001b[?2026hB");
        // Legacy callers resize the published screen before notification. A
        // failed notification cannot undo that caller-owned operation.
        screen.Resize(10, 3, reflowOnResize: true);
        byte[] before = processor.GetBinarySnapshot();
        TerminalRow published = screen.GetViewportRow(0);
        FailAt(processor, ManagedResizeCheckpoint.Ready);
        Assert.Throws<OutOfMemoryException>(() => processor.NotifyResize(10, 3, 150, 90));
        Assert.Same(published, screen.GetViewportRow(0));
        Assert.Equal(before, processor.GetBinarySnapshot());
        Assert.Equal(10, screen.Columns);
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        processor.ResizeCheckpoint = null;
        processor.NotifyResize(10, 3, 150, 90);
        Assert.Equal(hold ? 'B' : 0, screen.GetViewportRow(0).ReadOnlyCells[1].Codepoint);
        Assert.Null(processor.NextTimedRefreshDelay);
        Assert.Equal("\u001b[48;3;10;90;150t", Assert.Single(replies));
    }

    [Fact]
    public void AResponseObserverSeesCommittedStateAndCannotRollItBackByThrowing()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        Write(processor, "\u001b[?2048hA\u001b[?2026hB");
        TerminalGridPosition[] positions = [new(1, 0)];
        InvalidOperationException failure = new("Host response sink failed");
        processor.ResponseCallback = bytes =>
        {
            Assert.Equal("\u001b[48;3;10;60;100t", Encoding.ASCII.GetString(bytes));
            Assert.Equal(10, screen.Columns);
            Assert.Equal(3, screen.ViewportRows);
            Assert.Equal('B', screen.GetViewportRow(0).ReadOnlyCells[1].Codepoint);
            Assert.Null(processor.NextTimedRefreshDelay);
            Write(processor, "C"); // Reentrant observers operate on committed state.
            throw failure;
        };
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.ResizeScreen(10, 3, 100, 60, true, positions)));
        processor.ResponseCallback = null;
        Assert.Equal('C', screen.GetViewportRow(0).ReadOnlyCells[2].Codepoint);
        Assert.Equal(10, screen.Columns);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedGraphicsPublicationPreservesAnimationDeadlineAndPixels(bool hold)
    {
        ManualClock clock = new();
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { TimeProvider = clock });
        StartAnimation(processor);
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? retained));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, retained!.RgbaPixels);
        if (hold) Write(processor, "\u001b[?2026h");
        clock.Advance(40);
        TimeSpan? before = processor.NextTimedRefreshDelay;
        FailAt(processor, ManagedResizeCheckpoint.Graphics);
        Assert.Throws<OutOfMemoryException>(() => processor.ResizeScreen(10, 3, 100, 60, true));
        Assert.Equal(before, processor.NextTimedRefreshDelay);
        Assert.True(screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? after));
        Assert.Same(retained, after);
        processor.ResizeCheckpoint = null;
        processor.ResizeScreen(10, 3, 100, 60, true);
        Assert.True(screen.TryGetKittyImageSource(1, out after));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, after!.RgbaPixels);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, retained.RgbaPixels);
        Assert.Equal(TimeSpan.FromMilliseconds(40), processor.NextTimedRefreshDelay);
        clock.Advance(40);
        Assert.True(processor.RefreshTimedState());
        Assert.True(screen.TryGetKittyImageSource(1, out after));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, after!.RgbaPixels);
        Write(processor, "\u001b[?47h\u001b[?47l");
        Assert.True(screen.TryGetKittyImageSource(1, out after));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, after!.RgbaPixels);
    }

    [Fact]
    public void FailedShrinkDoesNotRetireTheOriginalPlacement()
    {
        TerminalScreen screen = new(8, 3, scrollbackLimit: 0);
        using BasicVtProcessor processor = new(screen);
        // Keep the bottom cursor row, so shrinking a no-history screen retires
        // the image's top row. A bottom image pin would prevent blank trimming.
        Write(processor, "\u001b_Ga=T,i=1,f=32,s=1,v=1,C=1;/wAA/w==\u001b\\\u001b[3;1H");
        Assert.Equal(1, screen.GetKittyPlacements().Length);
        FailAt(processor, ManagedResizeCheckpoint.Graphics);
        Assert.Throws<OutOfMemoryException>(() => processor.ResizeScreen(8, 1, 80, 20, false));
        Assert.Equal(1, screen.GetKittyPlacements().Length);
        processor.ResizeCheckpoint = null;
        // Republish from the live store, not just the retained screen recipe.
        processor.ResizeScreen(8, 3, 80, 60, false);
        Assert.Equal(1, screen.GetKittyPlacements().Length);
        processor.ResizeScreen(8, 1, 80, 20, false);
        Assert.Empty(screen.GetKittyPlacements().ToArray());
    }

    [Fact]
    public void FailedColumnModeResizeDoesNotChangeItsModeBit()
    {
        TerminalScreen screen = new(80, 3);
        using BasicVtProcessor processor = new(screen);
        Write(processor, "\u001b[?40h");
        FailAt(processor, ManagedResizeCheckpoint.Ready);
        Assert.Throws<OutOfMemoryException>(() => Write(processor, "\u001b[?3h"));
        Assert.Equal(80, screen.Columns);
        processor.ResizeCheckpoint = null;
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        Write(processor, "\u001b[?3$p");
        Assert.Equal("\u001b[?3;2$y", Assert.Single(replies));
        Write(processor, "\u001b[?3h");
        Assert.Equal(132, screen.Columns);
    }

    [Fact]
    public void TimeProviderFailureAfterLayoutIsAlsoRolledBack()
    {
        ManualClock clock = new();
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen, new() { TimeProvider = clock });
        Write(processor, "original");
        byte[] before = processor.GetBinarySnapshot();
        clock.Fail = true;
        Assert.Throws<InvalidOperationException>(() => processor.ResizeScreen(4, 3, 40, 60, true));
        clock.Fail = false;
        Assert.Equal(before, processor.GetBinarySnapshot());
        processor.ResizeScreen(4, 3, 40, 60, true);
        Assert.Equal(4, screen.Columns);
    }

    [Fact]
    public void ChunkedImageTransferSurvivesSuccessfulAndFailedResize()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        Write(processor, "\u001b_Ga=T,i=2,f=32,s=1,v=1,m=1;AQID\u001b\\");
        FailAt(processor, ManagedResizeCheckpoint.Ready);
        Assert.Throws<OutOfMemoryException>(() => processor.ResizeScreen(10, 3, 100, 60, true));
        processor.ResizeCheckpoint = null;
        processor.ResizeScreen(10, 3, 100, 60, true);
        Write(processor, "\u001b_Gm=0;BA==\u001b\\");
        Assert.True(screen.TryGetKittyImageSource(2, out TerminalKittyImageSource? image));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image!.RgbaPixels);
    }

    private static void StartAnimation(BasicVtProcessor processor)
        => Write(processor, "\u001b_Ga=T,i=1,f=32,s=1,v=1,C=1;/wAA/w==\u001b\\" +
            "\u001b_Ga=f,i=1,f=32,s=1,v=1,z=40;AAD//w==\u001b\\" +
            "\u001b_Ga=a,i=1,r=1,z=40,s=3\u001b\\");

    private static void FailAt(BasicVtProcessor processor, ManagedResizeCheckpoint checkpoint)
        => processor.ResizeCheckpoint = phase => { if (phase == checkpoint) throw new OutOfMemoryException("Injected resize allocation failure"); };

    private static void Write(BasicVtProcessor processor, string input) => processor.Process(Encoding.UTF8.GetBytes(input));

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        internal bool Fail { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Fail ? throw new InvalidOperationException("Clock failed") : _timestamp;
        internal void Advance(int milliseconds) => _timestamp += TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }
}
