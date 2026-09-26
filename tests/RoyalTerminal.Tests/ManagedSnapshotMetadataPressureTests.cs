// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// PAGE's optional-detail loss follows Ghostty, not CLR heap availability.
// Windows Terminal ROW and xterm.js BufferLine store attributes/links separately
// and do not define Ghostty's refcount/set/string-allocation snapshot contract.
public sealed class ManagedSnapshotMetadataPressureTests
{
    private static GhosttySnapshotStyle Bold => new(default, default, default, 1);
    private static GhosttySnapshotStyle Italic => new(default, default, default, 2);
    private static GhosttySnapshotStyle Faint => new(default, default, default, 4);
    private sealed record Link(ushort Id, uint ImplicitId, byte[]? ExplicitId, byte[] Uri);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(4, true)]
    public void StyleCapacityAndValueDeduplicationDoNotEraseRawStyles(int capacity, bool fits)
    {
        GhosttySnapshotPage page = Read(Payload(styleCapacity: (ushort)capacity,
            styles: [(1, Bold), (2, Bold), (3, Italic), (4, Faint)], styleIds: [1, 2, 3, 4]));
        TerminalRow row = Live(page);
        Assert.Equal(4, page.StyleCount);
        Assert.Equal(fits ? CellAttributes.Bold : CellAttributes.None, row.ReadOnlyCells[0].Attributes);
        Assert.Equal(row.ReadOnlyCells[0].Attributes, row.ReadOnlyCells[1].Attributes);
        Assert.Equal(fits ? CellAttributes.Italic : CellAttributes.None, row.ReadOnlyCells[2].Attributes);
        Assert.Equal(CellAttributes.None, row.ReadOnlyCells[3].Attributes);
        Assert.True(page.TryGetStyle(4, out GhosttySnapshotStyle raw));
        Assert.Equal(Faint, raw);
        using MemoryStream encoded = new();
        page.WritePayloadTo(encoded);
        Assert.True(Read(encoded.ToArray()).TryGetStyle(4, out raw));
        Assert.Equal(Faint, raw);
    }

    [Fact]
    public void UnreferencedStylesStillOccupyTheTableWhileCellsAreRestored()
    {
        GhosttySnapshotPage page = Read(Payload(styleCapacity: 4,
            styles: [(1, Bold), (2, Italic), (3, Faint)], styleIds: [3, 0, 0, 0]));
        Assert.Equal(CellAttributes.None, Live(page).ReadOnlyCells[0].Attributes);
        Assert.True(page.TryGetStyle(3, out _));
    }

    [Fact]
    public void RestoredAllocationRetainsDeadStyleSlotsAfterTableReferencesAreReleased()
    {
        GhosttySnapshotPage page = Read(Payload(styleCapacity: 4,
            styles: [(1, Bold), (2, Italic)], styleIds: [2, 0, 0, 0]));
        TerminalRow row = Live(page);
        GhosttySnapshotStyleStorage storage = row.SnapshotAllocation!.CopyRestoredStyles();
        Assert.Equal(1, storage.Count);
        Assert.Equal(Italic, storage.CellStyle(0));
        Assert.Equal(GhosttySnapshotSetAddResult.NeedsRehash, storage.ChangeCursor(Faint));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.Rebuild(page.Capacity.Styles, out GhosttySnapshotStyleStorage? rehashed));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, rehashed!.ChangeCursor(Faint));
        // A second fork must see the original dead slot, not the first fork's rehash.
        GhosttySnapshotStyleStorage untouched = row.SnapshotAllocation.CopyRestoredStyles();
        Assert.Equal(GhosttySnapshotSetAddResult.NeedsRehash, untouched.ChangeCursor(Faint));
        Assert.Equal(2, page.StyleCount);
        Assert.True(page.TryGetStyle(1, out GhosttySnapshotStyle raw));
        Assert.Equal(Bold, raw);
    }

    [Fact]
    public void EqualWireStylesReleaseSeparateTableReferencesButPreserveBothCells()
    {
        GhosttySnapshotPage page = Read(Payload(styleCapacity: 4,
            styles: [(1, Bold), (2, Bold), (3, Italic)], styleIds: [1, 2, 0, 0]));
        TerminalRow row = Live(page);
        GhosttySnapshotStyleStorage storage = row.SnapshotAllocation!.CopyRestoredStyles();
        Assert.Equal(1, storage.Count);
        Assert.Equal(2, storage.CellCount);
        storage.ClearCell(0);
        Assert.Equal(Bold, storage.CellStyle(1));
        storage.ClearCell(1);
        Assert.Equal(0, storage.Count);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.ChangeCursor(Faint));
        Assert.Equal(1, storage.Count);
        Assert.Equal(1, row.SnapshotAllocation.CopyRestoredStyles().Count);
    }

    [Fact]
    public void RestoredStyleReferenceModelMatchesNativeCursorWritesRehashAndErase()
    {
        RequireNative();
        byte[][] inputs =
        [
            Payload(styleCapacity: 0),
            Payload(styleCapacity: 4, styles: [(1, Bold), (2, Italic)], styleIds: [2, 0, 0, 0]),
            Payload(styleCapacity: 4, styles: [(1, Bold), (2, Italic)], styleIds: [1, 0, 0, 0]),
            Payload(styleCapacity: 4, styles: [(1, Bold), (2, Italic)], styleIds: [1, 2, 1, 2]),
            Payload(styleCapacity: 4, styles: [(1, Bold), (2, Bold), (3, Italic)], styleIds: [1, 2, 0, 0]),
        ];
        foreach (byte[] input in inputs)
        {
            GhosttySnapshotPage page = Read(input);
            GhosttySnapshotStyleStorage storage = page.CreateAllocationIdentity().CopyRestoredStyles();
            GhosttySnapshotPageCapacity capacity = page.Capacity;
            // Style growth arithmetic is independent of page alignment here;
            // the test compares the capacity hint, not pooled byte charges.
            GhosttySnapshotAllocation layout = new(4096);
            using GhosttyTerminal native = GhosttySnapshot.Decode(Snapshot(input, 4));
            SetPen(Faint, "\u001b[0;2m");
            Write(2);
            SetPen(default, "\u001b[0m");
            SetPen(Bold, "\u001b[1m");
            Write(3);
            SetPen(Italic, "\u001b[0;3m");
            Write(0);
            SetPen(default, "\u001b[0m");
            native.Write("\u001b[2K"u8);
            for (int i = 0; i < 4; i++) storage.ClearCell(i);
            Compare();
            SetPen(Bold, "\u001b[1m");
            SetPen(Faint, "\u001b[0;2m");
            SetPen(default, "\u001b[0m");

            void SetPen(GhosttySnapshotStyle pen, string sgr)
            {
                // For these composed reset+set controls the intermediate default
                // only releases the previous cursor, exactly as ChangeCursor does.
                GhosttySnapshotSetAddResult result = storage.ChangeCursor(pen);
                if (result != GhosttySnapshotSetAddResult.Success)
                {
                    if (result == GhosttySnapshotSetAddResult.OutOfMemory)
                        Assert.True(layout.TryIncreaseCapacity(capacity, GhosttySnapshotCapacityDimension.Styles,
                            (ulong)storage.Count, 1, out capacity));
                    Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.Rebuild(capacity.Styles, out GhosttySnapshotStyleStorage? rebuilt));
                    storage = rebuilt!;
                    Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.ChangeCursor(pen));
                }
                native.Write(Encoding.UTF8.GetBytes(sgr));
                Compare();
            }

            void Write(int column)
            {
                native.Write(Encoding.UTF8.GetBytes($"\u001b[1;{column + 1}HX"));
                storage.WriteCursorToCell(column);
                Compare();
            }

            void Compare()
            {
                using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
                GhosttySnapshotScreen screen = reader.ReadReady().Screens[0];
                GhosttySnapshotPage actual = Assert.Single(screen.Pages);
                Assert.Equal(capacity.Styles, actual.Capacity.Styles);
                Assert.Equal(storage.Cursor, screen.State.Pen);
                Assert.Equal(storage.Count, actual.StyleCount);
                for (int i = 0; i < 4; i++)
                {
                    actual.TryGetStyle((ushort)(actual.Grid.Cells[i] >> 26), out GhosttySnapshotStyle expected);
                    Assert.Equal(expected, storage.CellStyle(i));
                }
            }
        }
    }

    [Theory]
    [InlineData(2048U, false)]
    [InlineData(4096U, true)]
    public void DuplicateLinkValueMustAllocateTemporaryStringsBeforeDeduplication(uint bytes, bool duplicateFits)
    {
        byte[] uri = Filled(2000, (byte)'a');
        GhosttySnapshotPage page = Read(Payload(stringCapacity: bytes,
            links: [new(1, 1, null, uri), new(2, 1, null, uri), new(3, 2, null, "z"u8.ToArray())], linkIds: [1, 2, 3, 0]));
        TerminalRow row = Live(page);
        Assert.NotEqual(0, row.ReadOnlyCells[0].HyperlinkId);
        Assert.Equal(duplicateFits, row.ReadOnlyCells[1].HyperlinkId != 0);
        Assert.NotEqual(0, row.ReadOnlyCells[2].HyperlinkId);
        if (duplicateFits) Assert.Equal(row.ReadOnlyCells[0].HyperlinkId, row.ReadOnlyCells[1].HyperlinkId);
        Assert.True(page.TryGetHyperlink(2, out _));
    }

    [Fact]
    public void IgnoredZeroIdRetainsDeadStringsUntilAnotherLinkReachesSetInsertion()
    {
        GhosttySnapshotPage page = Read(Payload(links:
            [new(0, 1, null, Filled(2000, 65)), new(1, 2, null, Filled(64, 66)), new(2, 3, null, Filled(32, 67))],
            linkIds: [1, 2, 0, 0]));
        TerminalRow row = Live(page);
        Assert.Equal(0, row.ReadOnlyCells[0].HyperlinkId); // two chunks cannot fit before trim
        Assert.NotEqual(0, row.ReadOnlyCells[1].HyperlinkId); // one chunk fits, allowing trim
        Assert.True(page.TryGetHyperlink(1, out _));
    }

    [Fact]
    public void IgnoredDuplicateWireIdCanExhaustStringsForFollowingEntries()
    {
        GhosttySnapshotPage page = Read(Payload(links:
            [new(1, 1, null, Filled(2000, 65)), new(1, 2, null, Filled(32, 66)), new(2, 3, null, [67])], linkIds: [1, 2, 0, 0]));
        TerminalRow row = Live(page);
        Assert.NotEqual(0, row.ReadOnlyCells[0].HyperlinkId);
        Assert.Equal(0, row.ReadOnlyCells[1].HyperlinkId);
        Assert.Equal(2, page.HyperlinkCount);
    }

    [Fact]
    public void FailedExplicitUriFreesItsIdAllocationForTheNextEntry()
    {
        GhosttySnapshotPage page = Read(Payload(links:
            [new(1, 0, Filled(64, 65), Filled(2048, 66)), new(2, 3, null, Filled(2048, 67))], linkIds: [1, 2, 0, 0]));
        TerminalRow row = Live(page);
        Assert.Equal(0, row.ReadOnlyCells[0].HyperlinkId);
        Assert.NotEqual(0, row.ReadOnlyCells[1].HyperlinkId);
    }

    [Theory]
    [InlineData(2048U, 0, 2049, false)]
    [InlineData(2048U, 0, 2049, true)]
    [InlineData(4096U, 0, 4097, false)]
    [InlineData(4096U, 32, 4065, false)]
    [InlineData(4096U, 2016, 2081, true)]
    [InlineData(6144U, 0, 6145, false)]
    [InlineData(6144U, 2016, 4129, false)]
    [InlineData(6144U, 4064, 2081, true)]
    public void OversizedLinkSpanIsDroppedWithoutConsumingTheRemainingStrings(uint capacity, int prefix, int oversized, bool explicitId)
    {
        GhosttySnapshotPage page = Read(LargeSpanPayload(capacity, prefix, oversized, explicitId));
        TerminalRow row = Live(page);
        Assert.Equal(prefix != 0, row.ReadOnlyCells[0].HyperlinkId != 0);
        Assert.Equal(0, row.ReadOnlyCells[1].HyperlinkId);
        Assert.NotEqual(0, row.ReadOnlyCells[2].HyperlinkId);
        Assert.True(page.TryGetHyperlink(2, out GhosttySnapshotHyperlink raw));
        Assert.Equal(oversized, explicitId ? raw.ExplicitId.Length : raw.Uri.Length);
    }

    [Fact]
    public void NativeOversizedLinkSpansReturnAllocationFailureAtTheLastBitmapWord()
    {
        RequireNative();
        // The pinned allocator lacked a bounds check after searching complete
        // words. These cases require the repository-owned native overlay, not
        // a plain upstream binary: failure must omit detail, never read OOB.
        CompareNative(LargeSpanPayload(2048, 0, 2049, false));
        CompareNative(LargeSpanPayload(2048, 0, 2049, true));
        CompareNative(LargeSpanPayload(4096, 0, 4097, false));
        CompareNative(LargeSpanPayload(4096, 32, 4065, false));
        CompareNative(LargeSpanPayload(4096, 2016, 2081, true));
        CompareNative(LargeSpanPayload(6144, 0, 6145, false));
        CompareNative(LargeSpanPayload(6144, 2016, 4129, false));
        CompareNative(LargeSpanPayload(6144, 4064, 2081, true));
    }

    [Fact]
    public void HyperlinkCellMapAdmitsOnlyItsNativeLoadInRowOrder()
    {
        ushort[] ids = new ushort[128]; Array.Fill(ids, (ushort)1);
        GhosttySnapshotPage page = Read(Payload(columns: 128, links: [new(1, 1, null, [65])], linkIds: ids));
        TerminalRow row = Live(page);
        for (int i = 0; i < ids.Length; i++) Assert.Equal(i < 102, row.ReadOnlyCells[i].HyperlinkId != 0);
        for (int i = 0; i < ids.Length; i++) Assert.Equal(1UL, page.Grid.Cells[i] >> 48);
        GhosttySnapshotHyperlinkStorage storage = page.CreateAllocationIdentity().CopyRestoredHyperlinks();
        Assert.Equal(102, storage.CellCount);
        Assert.Equal(102, storage.ReferenceCount(storage.CellId(0)));
        Assert.Equal(0, storage.CellId(102));
        Assert.Equal(1, storage.Count);
    }

    [Fact]
    public void RestoredHyperlinkSeedReleasesAliasTableReferencesButRetainsDeadStrings()
    {
        GhosttySnapshotPage page = Read(Payload(links:
            [new(1, 1, null, [65]), new(2, 1, null, [65]), new(3, 2, null, [66])], linkIds: [1, 2, 0, 0]));
        GhosttySnapshotPageAllocation allocation = page.CreateAllocationIdentity();
        Assert.True(allocation.HasHyperlinkSeed);
        GhosttySnapshotHyperlinkStorage storage = allocation.CopyRestoredHyperlinks();
        Assert.Equal(1, storage.Count);
        Assert.Equal(2, storage.CellCount);
        Assert.Equal(storage.CellId(0), storage.CellId(1));
        Assert.Equal(2, storage.ReferenceCount(storage.CellId(0)));
        Assert.Equal(64UL, storage.StringBytes); // Unused B is dead, not freed.
        storage.ClearCells(0, 4);
        Assert.Equal(0, storage.Count);
        Assert.Equal(64UL, storage.StringBytes);
        GhosttySnapshotHyperlinkStorage retained = allocation.CopyRestoredHyperlinks();
        Assert.Equal(1, retained.Count);
        Assert.Equal(2, retained.ReferenceCount(retained.CellId(0)));
        Assert.True(page.TryGetLiveCellHyperlink(0, out GhosttySnapshotHyperlink link));
        Assert.Equal(new byte[] { 65 }, link.Uri.ToArray());
        Assert.True(page.TryGetHyperlink(3, out _)); // Raw table remains lossless.
    }

    [Fact]
    public void IgnoredDuplicateWireIdLeavesItsStringPressureInTheRetainedSeed()
    {
        GhosttySnapshotPage page = Read(Payload(links:
            [new(1, 1, null, Filled(2000, 65)), new(1, 2, null, Filled(32, 66)), new(2, 3, null, [67])], linkIds: [1, 2, 0, 0]));
        GhosttySnapshotHyperlinkStorage storage = page.CreateAllocationIdentity().CopyRestoredHyperlinks();
        Assert.Equal(1, storage.Count);
        Assert.Equal(1, storage.CellCount);
        Assert.Equal(2048UL, storage.StringBytes);
        storage.Clear(0);
        Assert.Equal(0, storage.Count);
        Assert.Equal(2048UL, storage.StringBytes);
        using MemoryStream output = new();
        new GhosttySnapshotHyperlink(false, 4, [], [68]).WriteTo(output);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.StringsFull, storage.StartCursor(output.ToArray()));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.Rebuild(192, 2048, out GhosttySnapshotHyperlinkStorage? rebuilt));
        Assert.Equal(0UL, rebuilt!.StringBytes);
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, rebuilt.StartCursor(output.ToArray()));
        Assert.Equal(32UL, rebuilt.StringBytes);
    }

    [Fact]
    public void NativePackedStyleHashUsesZigIntegerMixer()
    {
        // PackedStyle tags=palette/none/none, palette index=0 folds to u64(1).
        Assert.Equal(0x071894DE00D9981FUL,
            GhosttySnapshotMetadataHash.Style(new(new(1, 0, 0, 0), default, default, 0)));
        Assert.Equal(0UL, GhosttySnapshotMetadataHash.Style(default));
    }

    [Fact]
    public void NativePressureMatchesStyleHyperlinkAndStringRestoration()
    {
        RequireNative();
        byte[] large = Filled(2000, 65);
        byte[][] cases =
        [
            Payload(styleCapacity: 0, styles: [(1, Bold)], styleIds: [1, 0, 0, 0]),
            Payload(styleCapacity: 4, styles: [(1, Bold), (2, Bold), (3, Italic), (4, Faint)], styleIds: [1, 2, 3, 4]),
            Payload(linkCapacity: 0, links: [new(1, 1, null, [65])], linkIds: [1, 0, 0, 0]),
            Payload(stringCapacity: 0, links: [new(1, 1, null, [65])], linkIds: [1, 0, 0, 0]),
            Payload(links: [new(1, 1, null, large), new(2, 1, null, large), new(3, 2, null, [65])], linkIds: [1, 2, 3, 0]),
            Payload(stringCapacity: 4096, links: [new(1, 1, null, large), new(2, 1, null, large), new(3, 2, null, [65])], linkIds: [1, 2, 3, 0]),
            Payload(links: [new(0, 1, null, large), new(1, 2, null, Filled(64, 66)), new(2, 3, null, Filled(32, 67))], linkIds: [1, 2, 0, 0]),
            Payload(links: [new(1, 1, null, large), new(1, 2, null, Filled(32, 66)), new(2, 3, null, [67])], linkIds: [1, 2, 0, 0]),
            Payload(links: [new(1, 0, Filled(64, 65), Filled(2048, 66)), new(2, 3, null, Filled(2048, 67))], linkIds: [1, 2, 0, 0]),
            Payload(stringCapacity: 4096, links: [new(1, 0, [65], Filled(2080, 66))], linkIds: [1, 0, 0, 0]),
            Payload(links: [new(1, 0, [], [65]), new(1, 1, null, [66]), new(2, 0, [67], [68])], linkIds: [1, 2, 0, 0]),
        ];
        foreach (byte[] payload in cases) CompareNative(payload);
        ushort[] ids = new ushort[128]; Array.Fill(ids, (ushort)1);
        CompareNative(Payload(columns: 128, links: [new(1, 1, null, [65])], linkIds: ids));
    }

    [Fact]
    public void NativeStyleProbeLimitMatchesEvenBelowNominalCapacity()
    {
        RequireNative();
        List<(ushort, GhosttySnapshotStyle)> entries = [];
        for (int rgb = 0; entries.Count < 33 && rgb < 1_000_000; rgb++)
        {
            GhosttySnapshotStyle style = new(new(2, (byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16)), default, default, 1);
            if ((GhosttySnapshotMetadataHash.Style(style) & 127) == 0) entries.Add(((ushort)(entries.Count + 1), style));
        }
        Assert.Equal(33, entries.Count);
        entries.Add((34, entries[0].Item2)); // lookup succeeds despite the maximum probe
        ushort[] ids = new ushort[34];
        for (int i = 0; i < ids.Length; i++) ids[i] = (ushort)(i + 1);
        byte[] payload = Payload(columns: 34, styleCapacity: 128, styles: entries.ToArray(), styleIds: ids);
        TerminalRow row = Live(Read(payload));
        Assert.Equal(CellAttributes.None, row.ReadOnlyCells[32].Attributes);
        Assert.Equal(CellAttributes.Bold, row.ReadOnlyCells[33].Attributes);
        CompareNative(payload);
    }

    [Fact]
    public void NativeHyperlinkProbeLimitMatchesWyhashIncludingSliceLengths()
    {
        RequireNative();
        List<Link> entries = [];
        for (int value = 0; entries.Count < 33 && value < 1_000_000; value++)
        {
            byte[] uri = Encoding.ASCII.GetBytes("https://example.com/" + value);
            GhosttySnapshotHyperlink link = new(false, 1, [], uri);
            if ((GhosttySnapshotMetadataHash.Hyperlink(link) & 127) == 0)
                entries.Add(new((ushort)(entries.Count + 1), 1, null, uri));
        }
        Assert.Equal(33, entries.Count);
        entries.Add(entries[0] with { Id = 34 });
        ushort[] ids = new ushort[34];
        for (int i = 0; i < ids.Length; i++) ids[i] = (ushort)(i + 1);
        byte[] payload = Payload(columns: 34, linkCapacity: 48 * 128, stringCapacity: 65536,
            links: entries.ToArray(), linkIds: ids);
        TerminalRow row = Live(Read(payload));
        Assert.Equal(0, row.ReadOnlyCells[32].HyperlinkId);
        Assert.NotEqual(0, row.ReadOnlyCells[33].HyperlinkId);
        CompareNative(payload);
    }

    [Fact]
    public void SeededMetadataTablesAndDuplicateIdsMatchNative()
    {
        RequireNative();
        Random random = new(0x50414745);
        ushort[] capacities = [0, 1, 4, 8, 16, 32];
        for (int sample = 0; sample < 48; sample++)
        {
            (ushort, GhosttySnapshotStyle)[] styles = new (ushort, GhosttySnapshotStyle)[20];
            Link[] links = new Link[20];
            for (int i = 0; i < 20; i++)
            {
                styles[i] = ((ushort)random.Next(0, 12), new(new(1, (byte)random.Next(8), 0, 0), default, default, (ushort)random.Next(8)));
                links[i] = new((ushort)random.Next(0, 12), (uint)random.Next(4),
                    random.Next(2) == 0 ? null : Filled(random.Next(0, 65), (byte)random.Next(65, 69)),
                    Filled(random.Next(0, 300), (byte)random.Next(65, 69)));
            }
            ushort[] ids = new ushort[12];
            for (int i = 0; i < ids.Length; i++) ids[i] = (ushort)i;
            CompareNative(Payload(columns: 12, styleCapacity: capacities[sample % capacities.Length],
                linkCapacity: (ushort)(48 * (sample % 9)), stringCapacity: (uint)(sample % 5 * 1024),
                styles: styles, links: links, styleIds: ids, linkIds: ids));
        }
    }

    private static void CompareNative(byte[] payload)
    {
        int columns = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        byte[] bytes = Snapshot(payload, columns);
        using GhosttyTerminal native = GhosttySnapshot.Decode(bytes);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(bytes);
        Compare();
        native.Write("\u001b[1;1Hx\u001b[0m"u8); managed.Processor.Process("\u001b[1;1Hx\u001b[0m"u8);
        Compare();

        void Compare()
        {
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotPage page = Assert.Single(reader.ReadReady().Screens[0].Pages);
            TerminalRow row = managed.Screen.GetViewportRow(0);
            for (int i = 0; i < columns; i++)
            {
                ulong cell = page.Grid.Cells[i];
                page.TryGetStyle((ushort)(cell >> 26), out GhosttySnapshotStyle expected);
                Assert.Equal(expected, GhosttySnapshotLivePage.EncodeStyle(row.ReadOnlyCells[i]));
                Assert.Equal((int)((cell >> 2) & 0xFFFFFF), row.ReadOnlyCells[i].Codepoint);
                bool linked = page.TryGetHyperlink((ushort)(cell >> 48), out GhosttySnapshotHyperlink link);
                Assert.Equal(linked, managed.Screen.TryGetHyperlink(row.ReadOnlyCells[i].HyperlinkId, out TerminalHyperlink? actual));
                if (!linked) continue;
                Assert.Equal(link.Uri.ToArray(), actual!.UriBytes.ToArray());
                Assert.Equal(link.ExplicitId.ToArray(), actual.ExplicitId.ToArray());
                Assert.Equal(link.ImplicitId, actual.ImplicitId);
            }
        }
    }

    private static GhosttySnapshotPage Read(byte[] payload) => GhosttySnapshotPage.Read(payload, 65535, 1024, 4 * 1024 * 1024);
    private static TerminalRow Live(GhosttySnapshotPage page) => Assert.Single(GhosttySnapshotLivePage.Decode(page, new(page.Grid.Columns, 1)));
    private static byte[] Filled(int length, byte value) { byte[] bytes = new byte[length]; Array.Fill(bytes, value); return bytes; }

    private static byte[] LargeSpanPayload(uint capacity, int prefix, int oversized, bool explicitId)
    {
        List<Link> links = [];
        if (prefix != 0) links.Add(new(1, 1, null, Filled(prefix, 65)));
        links.Add(explicitId
            ? new(2, 0, Filled(oversized, 66), [67])
            : new(2, 2, null, Filled(oversized, 66)));
        // Fill all remaining chunks to detect partial allocation on failure.
        links.Add(new(3, 3, null, Filled(checked((int)capacity - prefix), 68)));
        return Payload(stringCapacity: capacity, links: links.ToArray(), linkIds: [1, 2, 3, 0]);
    }

    private static byte[] Payload(int columns = 4, ushort styleCapacity = 8, ushort linkCapacity = 192, uint stringCapacity = 2048,
        (ushort Id, GhosttySnapshotStyle Style)[]? styles = null, Link[]? links = null, ushort[]? styleIds = null, ushort[]? linkIds = null)
    {
        using MemoryStream output = new();
        using BinaryWriter writer = new(output, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)columns); writer.Write((ushort)1);
        writer.Write((ushort)(styles?.Length ?? 0)); writer.Write((ushort)(links?.Length ?? 0));
        writer.Write(styleCapacity); writer.Write(linkCapacity); writer.Write(0U); writer.Write(stringCapacity);
        Span<byte> styleBytes = stackalloc byte[16];
        foreach ((ushort id, GhosttySnapshotStyle style) in styles ?? [])
        {
            writer.Write(id); style.Write(styleBytes); output.Write(styleBytes);
        }
        foreach (Link link in links ?? [])
        {
            writer.Write(link.Id); writer.Write(link.ExplicitId is null ? (byte)1 : (byte)2);
            writer.Write(link.ExplicitId is null ? link.ImplicitId : (uint)link.ExplicitId.Length);
            if (link.ExplicitId is { } id) output.Write(id);
            writer.Write((uint)link.Uri.Length); output.Write(link.Uri);
        }
        writer.Write((byte)0x30); writer.Write((ushort)columns);
        for (int i = 0; i < columns; i++)
        {
            ushort style = styleIds is not null && i < styleIds.Length ? styleIds[i] : (ushort)0;
            ushort link = linkIds is not null && i < linkIds.Length ? linkIds[i] : (ushort)0;
            writer.Write(((ulong)'A' << 2) | ((ulong)style << 26) | ((ulong)link << 48) | (link == 0 ? 0 : 1UL << 45));
        }
        writer.Write(0U);
        return output.ToArray();
    }

    private static byte[] Snapshot(byte[] page, int columns)
    {
        using BasicVtProcessor source = new(new TerminalScreen(columns, 1));
        using GhosttySnapshotRecordReader reader = new(source.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            records.Add(new(tag, tag == GhosttySnapshotRecordTag.Page ? page : payload.ToArray()));
            if (tag == GhosttySnapshotRecordTag.Finish) return SnapshotTestRecords.Encode(records);
        }
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
