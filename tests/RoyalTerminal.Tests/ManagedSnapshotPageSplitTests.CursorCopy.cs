// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Screen.cursorCopy restores the destination cursor on style failure,
// unlike DECRC's default-style fallback. Terminal.switchScreen has already
// selected the new buffer, transferred charset and ended links at that point.
// WT AdaptDispatch::_SetAlternateScreenBufferMode and xterm.js InputHandler's
// buffer activation have different storage/failure contracts; follow Ghostty.
public sealed partial class ManagedSnapshotPageSplitTests
{
    [Theory]
    [InlineData(47, true, true)]
    [InlineData(47, false, true)]
    [InlineData(1047, true, true)]
    [InlineData(1047, false, true)]
    [InlineData(1049, true, true)]
    [InlineData(47, true, false)]
    [InlineData(47, false, false)]
    [InlineData(1049, true, false)]
    public void FailedScreenCursorCopyRetainsDestinationRegistersButCommitsTheSwitch(int mode, bool alternate, bool styled)
    {
        using BasicVtProcessor processor = CursorCopyCase(alternate, styled, out TerminalScreen screen,
            out GhosttySnapshotPageAllocation pressure);
        int key = alternate ? 1 : 0;
        GhosttySnapshotStyle previous = styled ? RetainedCursorPen : default;
        TerminalScreen retained = screen.CreateStateCopy();
        int retainedLink = retained.SnapshotCursorHyperlinkToken(key, 0);

        processor.Process(Encoding.ASCII.GetBytes($"\u001b[?{mode}{(alternate ? 'h' : 'l')}"));

        Assert.Equal(alternate, screen.AlternateBufferActive);
        GhosttySnapshotScreenState state = ReadCursor(processor, key);
        Assert.Equal((7, 0), (state.CursorX, state.CursorY));
        Assert.Equal(mode != 1049, state.PendingWrap);
        Assert.Equal(previous, state.Pen);
        Assert.True(state.Protected);
        Assert.Equal((byte)2, state.CursorStyle); // Underline, not the source bar.
        Assert.Equal((int)TerminalSemanticContent.Input, state.SemanticContent);
        Assert.True(state.SemanticContentClearEol);
        Assert.Equal(41U, state.HyperlinkImplicitCounter);
        Assert.Equal((ushort)0x803, state.Charset.Bits); // Charset transfer already committed.
        Assert.False(state.TryGetHyperlink(out _));
        Assert.Equal((byte)2, state.ProtectedMode); // Screen policy, not cursor-owned.
        Assert.Null(state.SavedCursor);
        Assert.Same(pressure, screen.GetViewportRow(0).SnapshotAllocation);
        Assert.False(pressure.MetadataOverflow);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(key, 0, previous));
        Assert.Equal(mode == 1049 ? 0 : 'Z', screen.GetViewportRow(0).ReadOnlyCells[2].Codepoint);
        Assert.True(retained.SnapshotCursorStyleIsCurrent(key, 0, previous));
        Assert.NotEqual(0, retainedLink);
        Assert.Equal(retainedLink, retained.SnapshotCursorHyperlinkToken(key, 0));
        Assert.Equal('Z', retained.GetSnapshotRows(key)![0].ReadOnlyCells[2].Codepoint);

        // Use the surviving cursor on a roomy page, verifying charset,
        // protection, semantic content, style and the next implicit identity.
        processor.Process("\u001b[2;1Hq\u001b]8;;new\u001b\\r"u8);
        TerminalRow row = screen.GetViewportRow(1);
        Assert.Equal(0x2500, row.ReadOnlyCells[0].Codepoint);
        Assert.Equal(previous, GhosttySnapshotLivePage.EncodeStyle(row.ReadOnlyCells[0]));
        Assert.True(row.ReadOnlyCells[0].IsProtected);
        Assert.Equal(TerminalSemanticContent.Input, row.ReadOnlyCells[0].SemanticContent);
        Assert.True(screen.TryGetHyperlink(row.ReadOnlyCells[1].HyperlinkId, out TerminalHyperlink? link));
        Assert.Equal(41U, link!.ImplicitId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureAtCopiedPositionDoesNotRollBackAnAlreadySuccessfulCursorCopy(bool alternate)
    {
        using BasicVtProcessor processor = CursorCopyCase(alternate, styled: true, out TerminalScreen screen,
            out GhosttySnapshotPageAllocation pressure, pressureAtCopiedPosition: true);
        int key = alternate ? 1 : 0;

        processor.Process(Encoding.ASCII.GetBytes($"\u001b[?47{(alternate ? 'h' : 'l')}"));

        GhosttySnapshotScreenState state = ReadCursor(processor, key);
        Assert.Equal((1, 1), (state.CursorX, state.CursorY));
        Assert.False(state.PendingWrap);
        Assert.Equal(default, state.Pen);
        Assert.False(state.Protected);
        Assert.Equal((byte)0, state.CursorStyle);
        Assert.Equal((int)TerminalSemanticContent.Output, state.SemanticContent);
        Assert.False(state.SemanticContentClearEol);
        Assert.Equal(7U, state.HyperlinkImplicitCounter);
        Assert.Same(pressure, screen.GetViewportRow(1).SnapshotAllocation);
        Assert.False(pressure.MetadataOverflow);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(key, 1, default));
    }

    [Fact]
    public void FailedCursorCopyDuringOutputHoldPublishesOnlyOnRelease()
    {
        using BasicVtProcessor processor = CursorCopyCase(true, true, out TerminalScreen screen, out _);
        processor.Process("\u001b[?2026h\u001b[?47h"u8);
        Assert.False(screen.AlternateBufferActive);
        Assert.Equal(TerminalCursorStyle.Bar, processor.CursorStyle);

        processor.Process("\u001b[?2026l"u8);

        Assert.True(screen.AlternateBufferActive);
        Assert.Equal(TerminalCursorStyle.Underline, processor.CursorStyle);
        Assert.Equal((7, 0), (processor.CursorCol, processor.CursorRow));
        Assert.Equal(RetainedCursorPen, ReadCursor(processor, 1).Pen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingStyleInFullDestinationTableStillCopiesEveryCursorRegister(bool alternate)
    {
        using BasicVtProcessor processor = CursorCopyCase(alternate, true, out TerminalScreen screen, out _);
        int key = alternate ? 1 : 0;
        processor.InstallSnapshotScreenState(CursorState(1 - key, destination: false, RetainedCursorPen));

        processor.Process(Encoding.ASCII.GetBytes($"\u001b[?47{(alternate ? 'h' : 'l')}"));

        GhosttySnapshotScreenState state = ReadCursor(processor, key);
        Assert.Equal((1, 1), (state.CursorX, state.CursorY));
        Assert.Equal(RetainedCursorPen, state.Pen);
        Assert.False(state.PendingWrap || state.Protected || state.SemanticContentClearEol);
        Assert.Equal((byte)0, state.CursorStyle);
        Assert.Equal((int)TerminalSemanticContent.Output, state.SemanticContent);
        Assert.Equal(7U, state.HyperlinkImplicitCounter);
        Assert.Equal((ushort)0x803, state.Charset.Bits);
        Assert.False(state.TryGetHyperlink(out _));
        Assert.True(screen.SnapshotCursorStyleIsCurrent(key, 1, RetainedCursorPen));
    }

    private static GhosttySnapshotStyle RetainedCursorPen => new(new(2, 1, 0, 0), default, default, 0);

    [Fact]
    public void FailedCopyAfterPageSplitKeepsTheSplitAndRestoresTheDestinationCursor()
    {
        TerminalScreen owner = new(1024, 4);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(1024, 4, 100, owner.Theme);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        GhosttySnapshotPageAllocation pressure = PressurePage(1024);
        TerminalRow[] primary = Rows(4, new(new(1024, 4, 16, 192, 1024, 2048))).ToArray();
        TerminalRow[] alternate = Rows(4, pressure).ToArray();
        foreach (TerminalRow row in alternate) row.SnapshotAllocationUnmodified = true;
        screen.InstallSnapshotRows(primary, alternate, 0);
        using BasicVtProcessor processor = new(screen);
        processor.InstallSnapshotScreenState(CursorState(1, true, RetainedCursorPen, x: 1023, y: 1));
        processor.InstallSnapshotScreenState(CursorState(0, false, Bold, x: 1, y: 3));
        TerminalScreen retained = screen.CreateStateCopy();

        processor.Process("\u001b[?47h"u8);

        GhosttySnapshotScreenState state = ReadCursor(processor, 1);
        Assert.Equal((1023, 1), (state.CursorX, state.CursorY));
        Assert.True(state.PendingWrap);
        Assert.Equal(RetainedCursorPen, state.Pen);
        Assert.Equal(41U, state.HyperlinkImplicitCounter);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(1, 1, RetainedCursorPen));
        Assert.Same(pressure, screen.GetViewportRow(1).SnapshotAllocation);
        Assert.NotSame(pressure, screen.GetViewportRow(2).SnapshotAllocation);
        Assert.False(pressure.MetadataOverflow);
        Assert.False(screen.GetViewportRow(2).SnapshotAllocation!.MetadataOverflow);
        Assert.Same(pressure, retained.GetSnapshotRows(1)![2].SnapshotAllocation);
        Assert.True(retained.SnapshotCursorStyleIsCurrent(1, 1, RetainedCursorPen));
    }

    private static BasicVtProcessor CursorCopyCase(bool alternate, bool styled, out TerminalScreen screen,
        out GhosttySnapshotPageAllocation pressure, bool pressureAtCopiedPosition = false)
    {
        TerminalScreen owner = new(8, 2);
        screen = TerminalScreen.CreateSnapshotStorage(8, 2, 100, owner.Theme);
        pressure = PressurePage(8);
        TerminalRow[] primary = [CursorRow(), CursorRow()], secondary = [CursorRow(), CursorRow()];
        TerminalRow[] destination = alternate ? secondary : primary;
        destination[pressureAtCopiedPosition ? 1 : 0].SnapshotAllocation = pressure;
        destination[0][2].Codepoint = 'Z';
        foreach (TerminalRow row in destination) row.SnapshotAllocationUnmodified = true;
        screen.InstallSnapshotRows(primary, secondary, 0);
        BasicVtProcessor processor = new(screen);
        if (!alternate) processor.Process("\u001b[?47h"u8);
        int key = alternate ? 1 : 0;
        processor.InstallSnapshotScreenState(CursorState(key, destination: true, styled ? RetainedCursorPen : default));
        processor.InstallSnapshotScreenState(CursorState(1 - key, destination: false, Bold));
        return processor;
    }

    private static TerminalRow CursorRow()
        => new(8) { SnapshotAllocation = new(new(8, 1, 16, 192, 1024, 2048)) };

    private static GhosttySnapshotScreenState CursorState(int key, bool destination, GhosttySnapshotStyle pen,
        int? x = null, int? y = null)
    {
        byte[] header = new byte[GhosttySnapshotScreenState.HeaderLength];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)key);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)(x ?? (destination ? 7 : 1)));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)(y ?? (destination ? 0 : 1)));
        header[16] = destination ? (byte)2 : (byte)0;
        header[17] = destination ? (byte)(1 | 2 | 4 | 16) : (byte)0;
        pen.Write(header.AsSpan(18));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(34), destination ? 41U : 7U);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(38), destination ? (ushort)0x800 : (ushort)0x803);
        header[40] = destination ? (byte)2 : (byte)0;
        using MemoryStream stream = new();
        stream.Write(header);
        new GhosttySnapshotHyperlink(false, destination ? 40U : 6U, default, "old"u8.ToArray()).WriteTo(stream);
        return GhosttySnapshotScreenState.Read(stream.ToArray(), 2, 100);
    }

    private static GhosttySnapshotScreenState ReadCursor(BasicVtProcessor processor, int key)
    {
        using GhosttySnapshotStateReader reader = new(processor.GetBinarySnapshot(), new());
        foreach (GhosttySnapshotScreen screen in reader.ReadReady().Screens)
            if (screen.State.Key == key) return screen.State;
        throw new InvalidOperationException("Missing requested SCREEN record.");
    }
}
