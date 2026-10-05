// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Screen.increaseCapacity commits a successful cell clone even when
// cursor-style reinsertion fails, then independently restores the cursor link.
// WT TextBuffer/ROW and xterm.js BufferLine store attributes without this PAGE
// allocator contract. Follow Ghostty, using deterministic probe pressure rather
// than process OOM or a test-only production failure hook.
public sealed class ManagedSnapshotPageRebuildTests
{
    private static GhosttySnapshotStyle Pen => new(default, new(1, 4, 0, 0), default, 1);
    private static GhosttySnapshotPageCapacity Capacity => new(40, 1, 64, 192, 0, 2048);

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void RebuildCommitsCellsAndRestoresCursorDetailsIndependently(bool restoreCursor, bool linkFailure)
    {
        GhosttySnapshotPageStorage source = CrowdedStorage(out _);
        byte[] link = Link(linkFailure ? 2016 : 16);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.Hyperlinks.StartCursor(link));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.Hyperlinks.WriteCursorToCell(0));

        Assert.True(source.Rebuild(Capacity, restoreCursor, out GhosttySnapshotPageStorage? rebuilt));

        Assert.Equal(default, rebuilt!.Styles.Cursor);
        Assert.Equal(Pen, source.Styles.Cursor);
        Assert.Equal(32, rebuilt.Styles.CellCount);
        for (int i = 0; i < 32; i++) Assert.Equal(source.Styles.CellStyle(i), rebuilt.Styles.CellStyle(i));
        Assert.NotEqual(0, rebuilt.Hyperlinks.CellId(0));
        Assert.Equal(restoreCursor && !linkFailure, rebuilt.Hyperlinks.CursorId != 0);
        Assert.NotEqual(0, source.Hyperlinks.CursorId);
        Assert.NotSame(source.AllocationIdentity, rebuilt.AllocationIdentity);
        rebuilt.Styles.ClearCell(0);
        Assert.NotEqual(default, source.Styles.CellStyle(0));
    }

    [Fact]
    public void CellCloneFailureStillRejectsReplacementWithoutMutatingSource()
    {
        GhosttySnapshotPageStorage source = CrowdedStorage(out _);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, source.Hyperlinks.StartCursor(Link(16)));

        Assert.False(source.Rebuild(Capacity with { Styles = 4 }, true, out GhosttySnapshotPageStorage? rebuilt));

        Assert.Null(rebuilt);
        Assert.Equal(Pen, source.Styles.Cursor);
        Assert.Equal(32, source.Styles.CellCount);
        Assert.NotEqual(0, source.Hyperlinks.CursorId);
    }

    [Fact]
    public void StyleAlreadyReferencedByACellSurvivesTheFullProbeChain()
    {
        GhosttySnapshotPageStorage source = CrowdedStorage(out _);
        for (int i = 31; i >= 0; i--) source.Styles.SwapCells(i, i + 1);
        source.Styles.WriteCursorToCell(0);

        Assert.True(source.Rebuild(Capacity, true, out GhosttySnapshotPageStorage? rebuilt));

        Assert.Equal(Pen, rebuilt!.Styles.Cursor);
        rebuilt.Styles.ClearCell(0);
        Assert.Equal(Pen, rebuilt.Styles.Cursor);
    }

    [Theory]
    [InlineData(4096, false)]
    [InlineData(4096, true)]
    [InlineData(16384, false)]
    [InlineData(16384, true)]
    public void GraphemeGrowthDropsOnlyTheLivePenAndKeepsPublishedCowState(int alignment, bool alternate)
    {
        using BasicVtProcessor processor = CreateProcessor(alternate, alignment, out TerminalScreen screen);
        TerminalRow row = screen.GetViewportRow(0);
        GhosttySnapshotPageAllocation old = row.SnapshotAllocation!;
        TerminalScreen retained = screen.CreateStateCopy();

        processor.Process(Encoding.UTF8.GetBytes("\u0301"));

        Assert.NotSame(old, row.SnapshotAllocation);
        Assert.False(row.SnapshotAllocation!.MetadataOverflow);
        Assert.Equal((ushort)64, row.SnapshotAllocation.Capacity.Styles);
        Assert.True(row.SnapshotAllocation.Capacity.GraphemeBytes > 0);
        Assert.Equal("A\u0301", row.ReadOnlyCells[32].Grapheme);
        Assert.Equal(default, ReadCursor(processor, alternate ? 1 : 0).Pen);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(alternate ? 1 : 0, 0, default));
        Assert.True(retained.SnapshotCursorStyleIsCurrent(alternate ? 1 : 0, 0, Pen));
        Assert.Null(retained.GetViewportRow(0).ReadOnlyCells[32].Grapheme);

        processor.Process("XY"u8);

        Assert.Equal(CellAttributes.None, row.ReadOnlyCells[33].Attributes);
        Assert.Equal(default, row.ReadOnlyCells[33].BackgroundIdentity);
        Assert.Equal(CellAttributes.None, row.ReadOnlyCells[34].Attributes);
        Assert.True(row.ReadOnlyCells[33].IsProtected); // Protection is not SGR style.
        for (int i = 0; i < 32; i++)
            Assert.Equal(retained.GetViewportRow(0).ReadOnlyCells[i].ForegroundIdentity, row.ReadOnlyCells[i].ForegroundIdentity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FollowingSgrAndSavedCursorUseTheDroppedPenWithinTheSameBatch(bool alternate)
    {
        using BasicVtProcessor processor = CreateProcessor(alternate, 4096, out TerminalScreen screen);

        processor.Process(Encoding.UTF8.GetBytes("\u0301\u001b7\u001b[3mX\u001b8Y"));

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal('Y', row.ReadOnlyCells[33].Codepoint);
        Assert.Equal(CellAttributes.None, row.ReadOnlyCells[33].Attributes);
        GhosttySnapshotScreenState cursor = ReadCursor(processor, alternate ? 1 : 0);
        Assert.Equal(default, cursor.Pen);
        Assert.Equal(default, cursor.SavedCursor!.Value.Pen);
        Assert.True(cursor.Protected);
        // The deliberate SGR is allowed to grow/rehash after degradation.
        Assert.True(row.SnapshotAllocation!.Capacity.Styles > 64);
        processor.Process("\u001b[3mZ"u8);
        Assert.Equal(CellAttributes.Italic, row.ReadOnlyCells[34].Attributes);
        Assert.Equal(default, row.ReadOnlyCells[34].BackgroundIdentity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OscLinkGrowthKeepsTheNewLinkButDoesNotResurrectTheRejectedPen(bool alternate)
    {
        using BasicVtProcessor processor = CreateProcessor(alternate, 4096, out TerminalScreen screen);
        string uri = new('u', 2049);

        processor.Process(Encoding.UTF8.GetBytes("\u001b]8;;" + uri + "\u001b\\X"));

        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(CellAttributes.None, row.ReadOnlyCells[33].Attributes);
        Assert.True(screen.TryGetHyperlinkUrl(row.ReadOnlyCells[33].HyperlinkId, out string? actual));
        Assert.Equal(uri, actual);
        Assert.Equal(default, ReadCursor(processor, alternate ? 1 : 0).Pen);
        Assert.False(row.SnapshotAllocation!.MetadataOverflow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DormantReplacementDropsOnlyItsOwnPenAndNotificationIsCowOwned(bool alternate)
    {
        using BasicVtProcessor processor = CreateProcessor(alternate, 4096, out TerminalScreen screen);
        int key = alternate ? 0 : 1;
        TerminalRow dormant = screen.GetSnapshotRows(key)![0];
        InstallCrowdedRow(dormant, screen);
        Assert.True(screen.SnapshotStyleChanged(key, 0, Pen, Pen));
        processor.InstallSnapshotScreenState(Cursor(key));
        TerminalScreen retained = screen.CreateStateCopy();
        GhosttySnapshotPageAllocation previous = dormant.SnapshotAllocation!;

        dormant.SnapshotAllocation = screen.SnapshotAllocationReplaced(previous, new(Capacity), [dormant]);
        TerminalScreen pendingCopy = screen.CreateStateCopy();

        Assert.False(dormant.SnapshotAllocation.MetadataOverflow);
        Assert.Equal(default, ReadCursor(processor, key).Pen);
        Assert.Equal(Pen, ReadCursor(processor, 1 - key).Pen);
        Assert.True(retained.SnapshotCursorStyleIsCurrent(key, 0, Pen));
        Assert.Equal((byte)(1 << key), pendingCopy.TakeSnapshotCursorStyleDrops());
        Assert.Equal((byte)0, pendingCopy.TakeSnapshotCursorStyleDrops());
        Assert.Equal((byte)0, screen.TakeSnapshotCursorStyleDrops());
        Assert.Equal((byte)0, retained.TakeSnapshotCursorStyleDrops());
    }

    [Fact]
    public void ExplicitStyleChangeSupersedesAnUnobservedDrop()
    {
        using BasicVtProcessor processor = CreateProcessor(false, 4096, out TerminalScreen screen);
        TerminalRow row = screen.GetViewportRow(0);
        row.SnapshotAllocation = screen.SnapshotAllocationReplaced(row.SnapshotAllocation!, new(Capacity), [row]);
        GhosttySnapshotStyle italic = new(default, default, default, 2);

        Assert.True(screen.SnapshotStyleChanged(0, 0, default, italic));

        Assert.Equal((byte)0, screen.TakeSnapshotCursorStyleDrops());
        Assert.True(screen.SnapshotCursorStyleIsCurrent(0, 0, italic));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ScreenCursorInstallationObservesLinkDrivenStyleLoss(bool alternate, bool dormant)
    {
        using BasicVtProcessor processor = CreateProcessor(alternate, 4096, out TerminalScreen screen);
        int active = alternate ? 1 : 0;
        int key = dormant ? 1 - active : active;
        if (dormant)
        {
            InstallCrowdedRow(screen.GetSnapshotRows(key)![0], screen);
            Assert.True(screen.SnapshotStyleChanged(key, 0, Pen, Pen));
        }

        processor.InstallSnapshotScreenState(Cursor(key, Link(2049)));

        GhosttySnapshotScreenState cursor = ReadCursor(processor, key);
        Assert.Equal(default, cursor.Pen);
        Assert.True(cursor.Protected);
        Assert.True(cursor.TryGetHyperlink(out GhosttySnapshotHyperlink link));
        Assert.Equal(2049, link.Uri.Length);
        if (dormant) Assert.Equal(Pen, ReadCursor(processor, active).Pen);
    }

    [Fact]
    public void OutputHoldKeepsThePublishedPenUntilRelease()
    {
        using BasicVtProcessor processor = CreateProcessor(false, 4096, out TerminalScreen screen);
        TerminalRow published = screen.GetViewportRow(0);

        processor.Process(Encoding.UTF8.GetBytes("\u001b[?2026h\u0301"));

        Assert.True(screen.SnapshotCursorStyleIsCurrent(0, 0, Pen));
        Assert.Null(published.ReadOnlyCells[32].Grapheme);
        Assert.Equal(default, ReadCursor(processor, 0).Pen);

        processor.Process("X\u001b[?2026l"u8);

        Assert.True(screen.SnapshotCursorStyleIsCurrent(0, 0, default));
        Assert.Equal("A\u0301", screen.GetViewportRow(0).ReadOnlyCells[32].Grapheme);
        Assert.Equal(CellAttributes.None, screen.GetViewportRow(0).ReadOnlyCells[33].Attributes);
        Assert.Null(published.ReadOnlyCells[32].Grapheme);
    }

    private static BasicVtProcessor CreateProcessor(bool alternate, int alignment, out TerminalScreen screen)
    {
        screen = new(40, 1)
        {
            SnapshotScrollbackQuota = new() { MaximumBytes = ulong.MaxValue, MaximumRows = 100, PageAlignment = alignment },
        };
        BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[?47h\u001b[?47l"u8); // Retain both buffers.
        if (alternate) processor.Process("\u001b[?47h"u8);
        InstallCrowdedRow(screen.GetViewportRow(0), screen);
        Assert.True(screen.SnapshotStyleChanged(alternate ? 1 : 0, 0, Pen, Pen));
        processor.InstallSnapshotScreenState(Cursor(alternate ? 1 : 0));
        return processor;
    }

    private static void InstallCrowdedRow(TerminalRow row, TerminalScreen screen)
    {
        GhosttySnapshotPageStorage storage = CrowdedStorage(out GhosttySnapshotStyle[] styles);
        for (int i = 0; i < styles.Length; i++)
        {
            TerminalCell cell = GhosttySnapshotLivePage.DecodeStyle(styles[i], screen.Theme);
            cell.Codepoint = 'A'; cell.Width = 1;
            row[i] = cell;
        }
        row[32].Codepoint = 'A'; row[32].Width = 1;
        row.SnapshotAllocation = new(Capacity, storage.Styles, restoredGraphemes: storage.Graphemes, restoredHyperlinks: storage.Hyperlinks);
        row.SnapshotAllocationRow = 0;
        row.SnapshotAllocationUnmodified = true;
    }

    private static GhosttySnapshotPageStorage CrowdedStorage(out GhosttySnapshotStyle[] styles)
    {
        GhosttySnapshotPageStorage storage = new(Capacity);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.Styles.ChangeCursor(Pen));
        // The cursor is inserted first, outside the following 32-bucket chain.
        // Rebuild copies cells first: probe 31 then blocks the cursor-only pen.
        ulong bucket = (GhosttySnapshotMetadataHash.Style(Pen) + 1) & 63;
        styles = new GhosttySnapshotStyle[32];
        int count = 0;
        for (int rgb = 1; rgb < 65536 && count < styles.Length; rgb++)
        {
            GhosttySnapshotStyle style = new(new(2, (byte)rgb, (byte)(rgb >> 8), 0), default, default, 0);
            if ((GhosttySnapshotMetadataHash.Style(style) & 63) != bucket) continue;
            int id = storage.Styles.AddTableReference(style);
            Assert.NotEqual(0, id);
            storage.Styles.AttachDecodedCell(count, id);
            storage.Styles.ReleaseTableReference(id);
            styles[count++] = style;
        }
        Assert.Equal(32, count);
        return storage;
    }

    private static GhosttySnapshotScreenState Cursor(int key, byte[]? link = null)
    {
        byte[] header = new byte[GhosttySnapshotScreenState.HeaderLength];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)key);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 33);
        header[17] = 2; // Protection must survive SGR fallback.
        Pen.Write(header.AsSpan(18));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(38), 0x800);
        using MemoryStream stream = new();
        stream.Write(header);
        if (link is null) stream.WriteByte(0); else stream.Write(link);
        return GhosttySnapshotScreenState.Read(stream.ToArray(), 1, 4096);
    }

    private static GhosttySnapshotScreenState ReadCursor(BasicVtProcessor processor, int key)
    {
        using GhosttySnapshotStateReader reader = new(processor.GetBinarySnapshot(), new());
        foreach (GhosttySnapshotScreen screen in reader.ReadReady().Screens)
            if (screen.State.Key == key) return screen.State;
        throw new InvalidOperationException("Missing SCREEN.");
    }

    private static byte[] Link(int length)
    {
        using MemoryStream stream = new();
        new GhosttySnapshotHyperlink(false, 7, default, Encoding.ASCII.GetBytes(new string('u', length))).WriteTo(stream);
        return stream.ToArray();
    }
}
