// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty ReflowCursor.reflowRow/moveLastRowToNewPage defines OutOfSpace:
// split the final row, or retain only accepted metadata on the first row.
// WT TextBuffer::Reflow and xterm.js Buffer._reflow do not have PAGE limits.
// Like the allocation walker tests, use narrow CLR rows with a wide logical
// stride: capacity hints must not materialize four-GiB pages or huge grids.
public sealed class ManagedSnapshotReflowFailureTests
{
    [Theory]
    [InlineData("grapheme", 4096)]
    [InlineData("style", 4096)]
    [InlineData("string", 4096)]
    [InlineData("map", 4096)]
    [InlineData("set", 4096)]
    [InlineData("grapheme", 16384)]
    [InlineData("style", 16384)]
    [InlineData("string", 16384)]
    [InlineData("map", 16384)]
    [InlineData("set", 16384)]
    public void FirstRowRefusalDropsOnlyUnacceptedMetadataAndKeepsWalking(string kind, int alignment)
    {
        Fixture fixture = new(kind, alignment);
        TerminalRow row = fixture.Run(split: false);
        int target = fixture.PressureCells + 1;
        TerminalCell original = fixture.Incoming.ReadOnlyCells[1], actual = row.ReadOnlyCells[target];
        Assert.Equal(original.Codepoint, actual.Codepoint);
        Assert.Equal(original.Width, actual.Width);
        Assert.True(actual.IsProtected);
        Assert.Equal(TerminalSemanticContent.Input, actual.SemanticContent);
        Assert.Equal(kind == "grapheme" ? null : original.Grapheme, actual.Grapheme);
        Assert.Equal(kind == "style" ? original.HyperlinkId : 0, actual.HyperlinkId);
        Assert.Equal(CellAttributes.None, actual.Attributes);
        Assert.Equal(default, actual.ForegroundIdentity);
        Assert.Equal(default, actual.UnderlineIdentity);
        Assert.False(row.SnapshotAllocation!.MetadataOverflow);
        Assert.Equal(fixture.Capacity, row.SnapshotAllocation.Capacity);
        // A refusal must not terminate the remaining run or poison the page.
        fixture.CopyFollowing(row, target + 1);
        Assert.Equal('Z', row.ReadOnlyCells[target + 1].Codepoint);
        Assert.Equal(CellAttributes.None, row.ReadOnlyCells[target + 1].Attributes);
        Assert.Equal(original.Grapheme, fixture.Incoming.ReadOnlyCells[1].Grapheme);
        Assert.Equal(original.HyperlinkId, fixture.Incoming.ReadOnlyCells[1].HyperlinkId);
        Assert.NotEqual(CellAttributes.None, original.Attributes);
    }

    [Theory]
    [InlineData("grapheme", 4096)]
    [InlineData("style", 4096)]
    [InlineData("string", 4096)]
    [InlineData("map", 4096)]
    [InlineData("set", 4096)]
    [InlineData("grapheme", 16384)]
    [InlineData("style", 16384)]
    [InlineData("string", 16384)]
    [InlineData("map", 16384)]
    [InlineData("set", 16384)]
    public void LaterRowMovesWithItsPrefixAndAcceptedCellMetadataOwnedExactlyOnce(string kind, int alignment)
    {
        Fixture fixture = new(kind, alignment);
        TerminalRow row = fixture.Run(split: true);
        TerminalRow previous = fixture.Destination[0];
        Assert.NotSame(previous.SnapshotAllocation, row.SnapshotAllocation);
        Assert.Equal(0, row.SnapshotAllocationRow);
        Assert.Equal(0, previous.SnapshotAllocationRow);
        Assert.Equal(fixture.Capacity, previous.SnapshotAllocation!.Capacity);
        Assert.False(previous.SnapshotAllocation.MetadataOverflow);
        Assert.False(row.SnapshotAllocation!.MetadataOverflow);
        for (int column = 0; column < 2; column++)
        {
            TerminalCell a = row.ReadOnlyCells[column], b = fixture.Incoming.ReadOnlyCells[column];
            Assert.Equal(b.Codepoint, a.Codepoint);
            Assert.Equal(b.Grapheme, a.Grapheme);
            Assert.Equal(b.HyperlinkId, a.HyperlinkId);
            Assert.Equal(b.Attributes, a.Attributes);
            Assert.Equal(b.IsProtected, a.IsProtected);
        }
        GhosttySnapshotPageStorage storage = fixture.Storage(row);
        Assert.Equal(2, storage.Graphemes.Count);
        Assert.Equal(kind == "grapheme" ? 512UL : 32UL, storage.Graphemes.AllocatedBytes);
        Assert.Equal(2, storage.Hyperlinks.CellCount);
        Assert.Equal(2, storage.Styles.CellCount);
        int firstLink = storage.Hyperlinks.CellId(0), secondLink = storage.Hyperlinks.CellId(1);
        Assert.Equal(firstLink == secondLink ? 2 : 1, storage.Hyperlinks.ReferenceCount(firstLink));
        Assert.Equal(firstLink == secondLink ? 2 : 1, storage.Hyperlinks.ReferenceCount(secondLink));
        GhosttySnapshotPageStorage old = fixture.Storage(previous);
        Assert.Equal(kind == "grapheme" ? 3 : 0, old.Graphemes.Count);
        Assert.Equal(kind == "map" ? 101 : kind == "set" ? 2 : kind == "string" ? 1 : 0, old.Hyperlinks.CellCount);
        Assert.Equal(2, fixture.IncomingStorage.Graphemes.Count);
        Assert.Equal(2, fixture.IncomingStorage.Hyperlinks.CellCount);
        fixture.CopyFollowing(row, 2);
        Assert.Equal('Z', row.ReadOnlyCells[2].Codepoint);
    }

    [Fact]
    public void StyleOnlyBulkFailureDoesNotResurrectStylesOrSkipTheNextRun()
    {
        Fixture fixture = new("style", 4096, extras: false);
        TerminalRow row = fixture.Run(split: false);
        int target = fixture.PressureCells + 1;
        Assert.Equal(CellAttributes.Bold, row.ReadOnlyCells[target - 1].Attributes);
        Assert.Equal(CellAttributes.None, row.ReadOnlyCells[target].Attributes);
        fixture.CopyFollowing(row, target + 1);
        Assert.Equal('Z', row.ReadOnlyCells[target + 1].Codepoint);
        Assert.Equal(2, fixture.Storage(row).Styles.Count);
    }

    [Fact]
    public void SplitCloneFailureDoesNotCommitAPartialDestination()
    {
        Fixture fixture = new("style", 4096, extras: false, unclonablePrefix: true);
        TerminalRow first = fixture.AppendPressure();
        TerminalRow current = fixture.AppendIncoming();
        fixture.CopyPrefix(current);
        GhosttySnapshotPageAllocation page = current.SnapshotAllocation!;
        TerminalCell original = fixture.Incoming.ReadOnlyCells[1];
        // A row assembled from several pages may contain a link while the
        // failing style's source page has zero hyperlink capacity. Cloning the
        // prefix into that source-derived page must fail, not publish overflow.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => fixture.CopyIncoming(current, 1));
        Assert.Contains("row metadata clone", failure.Message);
        Assert.Same(page, first.SnapshotAllocation);
        Assert.Same(page, current.SnapshotAllocation);
        Assert.Equal(1, current.SnapshotAllocationRow);
        Assert.Equal(original.Grapheme, fixture.Incoming.ReadOnlyCells[1].Grapheme);
        Assert.Equal(original.HyperlinkId, fixture.Incoming.ReadOnlyCells[1].HyperlinkId);
        Assert.Equal(original.Attributes, fixture.Incoming.ReadOnlyCells[1].Attributes);
        Assert.False(first.SnapshotAllocation!.MetadataOverflow);
    }

    [Theory]
    [InlineData("grapheme", false)]
    [InlineData("style", false)]
    [InlineData("style", true)]
    public void ScreenReflowCannotOverwriteDegradedMetadataWithTheSourcePayload(string kind, bool wide)
    {
        GhosttySnapshotAllocation layout = new(4096);
        GhosttySnapshotPageCapacity capacity = Ceiling(kind == "style" ? GhosttySnapshotCapacityDimension.Styles : GhosttySnapshotCapacityDimension.GraphemeBytes, layout);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(8, 2, 100, new TerminalScreen(8, 2).Theme);
        TerminalRow first = new(8) { SnapshotAllocation = new(capacity), WrapsToNext = true };
        TerminalRow second = new(8) { SnapshotAllocation = new(capacity with { Rows = 2 }) };
        int pressure = kind == "style" ? 2 : 3;
        for (int column = 0; column < pressure; column++)
        {
            first[column].Codepoint = 'P';
            if (kind == "style") first[column].Attributes = column == 0 ? CellAttributes.Bold : CellAttributes.Italic;
            else first[column].Grapheme = "P" + new string('\u0301', 64);
        }
        second[0].Codepoint = 'A'; second[0].Attributes = CellAttributes.Bold;
        second[1].Codepoint = wide ? 0x754C : 'B'; second[1].Attributes = CellAttributes.Dim;
        second[1].IsProtected = true;
        if (kind == "grapheme")
            for (int column = 0; column < 2; column++)
                second[column].Grapheme = ((char)('A' + column)).ToString() + new string('\u0301', 64);
        if (wide) { second[1].Width = 2; second[2].Width = 0; second[2].Attributes = CellAttributes.Dim; }
        second[wide ? 3 : 2].Codepoint = 'Z';
        screen.InstallSnapshotRows([first, second], null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        TerminalScreen retained = screen.CreateStateCopy();

        screen.Resize(capacity.Columns, 2);

        TerminalRow row = screen.GetSnapshotRows(0)![0];
        Assert.Equal(wide ? 0x754C : 'B', row.ReadOnlyCells[9].Codepoint);
        Assert.Null(row.ReadOnlyCells[9].Grapheme);
        Assert.Equal(CellAttributes.None, row.ReadOnlyCells[9].Attributes);
        Assert.True(row.ReadOnlyCells[9].IsProtected);
        Assert.Equal(wide ? (byte)2 : (byte)1, row.ReadOnlyCells[9].Width);
        if (wide)
        {
            Assert.Equal((byte)0, row.ReadOnlyCells[10].Width);
            Assert.Equal(CellAttributes.None, row.ReadOnlyCells[10].Attributes);
        }
        Assert.Equal('Z', row.ReadOnlyCells[wide ? 11 : 10].Codepoint);
        Assert.False(row.SnapshotAllocation!.MetadataOverflow);
        Assert.Equal(CellAttributes.Dim, retained.GetSnapshotRows(0)![1].ReadOnlyCells[1].Attributes);
        Assert.Equal(8, retained.Columns);
    }

    [Fact]
    public void ProcessorRollsBackBothRowsAndAnchorsWhenTheSplitCloneCannotFit()
    {
        GhosttySnapshotPageCapacity capacity = Ceiling(GhosttySnapshotCapacityDimension.Styles, new(4096));
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(8, 3, 100, new TerminalScreen(8, 3).Theme);
        TerminalRow pressure = new(8) { SnapshotAllocation = new(capacity) };
        pressure[0].Codepoint = 'P'; pressure[0].Attributes = CellAttributes.Bold;
        pressure[1].Codepoint = 'P'; pressure[1].Attributes = CellAttributes.Italic;
        TerminalRow prefix = new(8) { SnapshotAllocation = new(capacity), WrapsToNext = true };
        prefix[0].Codepoint = 'Q'; prefix[0].Attributes = CellAttributes.Bold;
        prefix[0].HyperlinkId = screen.RegisterHyperlink("p"u8, default, 1);
        TerminalRow target = new(8) { SnapshotAllocation = new(capacity with { Rows = 2, HyperlinkBytes = 0 }) };
        target[0].Codepoint = 'D'; target[0].Attributes = CellAttributes.Dim;
        screen.InstallSnapshotRows([pressure, prefix, target], null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        using BasicVtProcessor processor = new(screen);
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalGridPosition[] positions = [new(0, 1), new(0, 2)];
        TerminalScreenAnchor anchor = screen.CreateAnchor(1, 0)!;

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            processor.ResizeScreen(capacity.Columns, 3, 0, 0, true, positions));

        Assert.Contains("row metadata clone", failure.Message);
        Assert.Equal(new[] { new TerminalGridPosition(0, 1), new TerminalGridPosition(0, 2) }, positions);
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition position));
        Assert.Equal(new TerminalGridPosition(0, 1), position);
        Assert.Same(pressure, screen.GetSnapshotRows(0)![0]);
        Assert.Same(prefix, screen.GetSnapshotRows(0)![1]);
        Assert.Same(target, screen.GetSnapshotRows(0)![2]);
        Assert.Equal(8, screen.Columns);
        Assert.False(screen.SnapshotMutationFailed);
        processor.Process("Z"u8);
        Assert.Equal('Z', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal('P', retained.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        screen.ReleaseAnchor(anchor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASecondProbeLimitRefusalDropsOnlyThatFieldWithoutAnotherGrowth(bool hyperlinks)
    {
        TerminalScreen owner = new(64, 2);
        GhosttySnapshotAllocation layout = new(4096);
        GhosttySnapshotPageCapacity capacity = new(64, 1, 64, 3072, 1024, 4096);
        TerminalRow pressure = new(64) { SnapshotAllocation = new(capacity) };
        GhosttySnapshotPageStorage pressureStorage = new(capacity);
        int accepted = 0;
        for (uint candidate = 1; accepted < 32; candidate++)
        {
            if (hyperlinks)
            {
                byte[] uri = Encoding.ASCII.GetBytes("collision-" + candidate);
                TerminalHyperlink link = new(uri, default, candidate);
                if ((GhosttySnapshotMetadataHash.Hyperlink(GhosttySnapshotHyperlink.Read(link.SnapshotEncoding, out _)) & 127) != 0) continue;
                pressure[accepted].HyperlinkId = owner.RegisterHyperlink(uri, default, candidate);
                Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, pressureStorage.Hyperlinks.ObserveCell(accepted, link.SnapshotEncoding));
            }
            else
            {
                GhosttySnapshotStyle style = new(new(2, (byte)candidate, (byte)(candidate >> 8), (byte)(candidate >> 16)), default, default, 1);
                // Growth projection is capped at 32 times the old request:
                // collide through every possible first replacement table.
                if ((GhosttySnapshotMetadataHash.Style(style) & 2047) != 0) continue;
                pressure[accepted] = GhosttySnapshotLivePage.DecodeStyle(style, owner.Theme);
                Assert.Equal(GhosttySnapshotSetAddResult.Success, pressureStorage.Styles.ChangeCell(accepted, style));
            }
            pressure[accepted++].Codepoint = 'P';
        }
        TerminalRow incoming = new(64) { SnapshotAllocation = new(capacity) };
        incoming[0].Codepoint = 'A'; incoming[0].Grapheme = "A\u0301"; incoming[0].Attributes = CellAttributes.Italic;
        int token = owner.RegisterHyperlink("target"u8, default, uint.MaxValue);
        incoming[0].HyperlinkId = token;
        Assert.True(owner.TryGetHyperlink(token, out TerminalHyperlink? targetLink));
        GhosttySnapshotPageStorage incomingStorage = new(capacity);
        Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, incomingStorage.Graphemes.Set(0, 1));
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, incomingStorage.Hyperlinks.ObserveCell(0, targetLink!.SnapshotEncoding));
        Assert.Equal(GhosttySnapshotSetAddResult.Success, incomingStorage.Styles.ChangeCell(0, new(default, default, default, 2)));
        TerminalRowBuffer source = new(); source.Add(pressure); source.Add(incoming);
        GhosttySnapshotPageTracker tracker = new();
        tracker.InstallReflowPage(pressure.SnapshotAllocation!, pressureStorage, [pressure]);
        tracker.InstallReflowPage(incoming.SnapshotAllocation!, incomingStorage, [incoming]);
        GhosttySnapshotReflowAllocation reflow = new(source, 64, layout, tracker, owner);
        TerminalRow destination = new(64);
        reflow.Append(destination, pressure.SnapshotAllocation!);
        pressure.ReadOnlyCells[..32].CopyTo(destination.Cells);
        int run = 0;
        reflow.CopyMetadata(destination, 0, [new(0, pressure.SnapshotAllocation!, 0)], ref run, 0, 32);
        GhosttySnapshotCapacityDimension dimension = hyperlinks ? GhosttySnapshotCapacityDimension.HyperlinkBytes : GhosttySnapshotCapacityDimension.Styles;
        Assert.True(layout.TryIncreaseCapacity(destination.SnapshotAllocation!.Capacity, dimension, 32, 1, out GhosttySnapshotPageCapacity expected));
        destination[32] = incoming.ReadOnlyCells[0];
        run = 0;

        reflow.CopyMetadata(destination, 32, [new(0, incoming.SnapshotAllocation!, 0)], ref run, 0, 1);
        reflow.Finish([destination], owner.DefaultForeground, owner.DefaultBackground);

        Assert.Equal(expected, destination.SnapshotAllocation!.Capacity);
        Assert.False(destination.SnapshotAllocation.MetadataOverflow);
        Assert.Equal("A\u0301", destination.ReadOnlyCells[32].Grapheme);
        Assert.Equal(hyperlinks ? 0 : token, destination.ReadOnlyCells[32].HyperlinkId);
        Assert.Equal(hyperlinks ? CellAttributes.Italic : CellAttributes.None, destination.ReadOnlyCells[32].Attributes);
        Assert.Equal(1, incomingStorage.Hyperlinks.ReferenceCount(incomingStorage.Hyperlinks.CellId(0)));
    }

    private sealed class Fixture
    {
        internal readonly TerminalScreen Owner = new(128, 2);
        internal readonly GhosttySnapshotPageCapacity Capacity;
        internal readonly GhosttySnapshotPageStorage IncomingStorage;
        internal readonly TerminalRow Incoming;
        internal readonly int PressureCells;
        internal readonly List<TerminalRow> Destination = [];
        private readonly GhosttySnapshotAllocation _layout;
        private readonly GhosttySnapshotPageTracker _tracker = new();
        private readonly GhosttySnapshotReflowAllocation _reflow;
        private readonly TerminalRow _pressure;
        private readonly TerminalRow? _prefix;

        internal Fixture(string kind, int alignment, bool extras = true, bool unclonablePrefix = false)
        {
            _layout = new(alignment);
            GhosttySnapshotCapacityDimension dimension = kind switch
            {
                "style" => GhosttySnapshotCapacityDimension.Styles,
                "string" => GhosttySnapshotCapacityDimension.StringBytes,
                "map" or "set" => GhosttySnapshotCapacityDimension.HyperlinkBytes,
                _ => GhosttySnapshotCapacityDimension.GraphemeBytes,
            };
            Capacity = Ceiling(dimension, _layout);
            _pressure = new(128) { SnapshotAllocation = new(Capacity) };
            Incoming = new(128) { SnapshotAllocation = new(Capacity with { Rows = 2, HyperlinkBytes = unclonablePrefix ? (ushort)0 : (ushort)192 }) };
            PressureCells = kind switch { "grapheme" => 3, "style" or "set" => 2, "map" => 101, _ => 1 };
            for (int column = 0; column < PressureCells; column++)
            {
                _pressure[column].Codepoint = 'P';
                if (kind == "grapheme") _pressure[column].Grapheme = "P" + new string('\u0301', 64);
                if (kind == "style") _pressure[column].Attributes = column == 0 ? CellAttributes.Bold : CellAttributes.Italic;
            }
            int pressureLink = Link(kind == "string" ? new string('p', 1984) : "p", 1);
            if (kind is "string" or "map" or "set")
                for (int column = 0; column < PressureCells; column++)
                    _pressure[column].HyperlinkId = kind == "set" && column == 1 ? Link("q", 2) : pressureLink;
            for (int column = 0; column < 2; column++)
            {
                Incoming[column].Codepoint = 'A' + column;
                Incoming[column].Attributes = column == 0 ? CellAttributes.Bold : CellAttributes.Dim;
                if (extras)
                {
                    Incoming[column].Grapheme = ((char)('A' + column)).ToString() + new string('\u0301', kind == "grapheme" ? 64 : 1);
                    Incoming[column].HyperlinkId = column == 0 && kind == "set" ? pressureLink
                        : Link(column == 1 && kind == "string" ? new string('t', 128) : column == 0 ? "s" : "t", (uint)column + 3);
                }
            }
            Incoming[1].IsProtected = true;
            Incoming[1].SemanticContent = TerminalSemanticContent.Input;
            Incoming[2].Codepoint = 'Z';
            TerminalRowBuffer source = new();
            source.Add(_pressure); source.Add(Incoming);
            _tracker.InstallReflowPage(_pressure.SnapshotAllocation!, Seed(_pressure), [_pressure]);
            IncomingStorage = Seed(Incoming);
            _tracker.InstallReflowPage(Incoming.SnapshotAllocation!, IncomingStorage, [Incoming]);
            if (unclonablePrefix)
            {
                _prefix = new(128) { SnapshotAllocation = new(Capacity) };
                _prefix[0].Codepoint = 'Q';
                _prefix[0].Attributes = CellAttributes.Bold;
                _prefix[0].HyperlinkId = pressureLink;
                source.Add(_prefix);
                _tracker.InstallReflowPage(_prefix.SnapshotAllocation!, Seed(_prefix), [_prefix]);
            }
            _reflow = new(source, Capacity.Columns, _layout, _tracker, Owner);
        }

        internal TerminalRow Run(bool split)
        {
            TerminalRow first = AppendPressure();
            TerminalRow current = split ? AppendIncoming() : first;
            CopyIncoming(current, split ? 0 : PressureCells);
            _reflow.Finish(Destination, Owner.DefaultForeground, Owner.DefaultBackground);
            return current;
        }

        internal TerminalRow AppendPressure()
        {
            TerminalRow row = new(128);
            _reflow.Append(row, _pressure.SnapshotAllocation!);
            Destination.Add(row);
            CopyPressureTo(row);
            return row;
        }

        internal TerminalRow AppendIncoming()
        {
            TerminalRow row = new(128);
            _reflow.Append(row, Incoming.SnapshotAllocation!);
            Destination.Add(row);
            return row;
        }

        internal void CopyPressureTo(TerminalRow row) => Copy(row, 0, _pressure, 0, PressureCells);
        internal void CopyPrefix(TerminalRow row) => Copy(row, 0, _prefix!, 0, 1);
        internal void CopyIncoming(TerminalRow row, int column) => Copy(row, column, Incoming, 0, 2);
        internal void CopyFollowing(TerminalRow row, int column)
        {
            Copy(row, column, Incoming, 2, 1);
            _reflow.Finish(Destination, Owner.DefaultForeground, Owner.DefaultBackground);
        }

        private void Copy(TerminalRow row, int column, TerminalRow source, int start, int count)
        {
            source.ReadOnlyCells.Slice(start, count).CopyTo(row.Cells[column..]);
            GhosttySnapshotReflowAllocation.Source[] provenance = [new(0, source.SnapshotAllocation!, 0)];
            int sourceRun = 0;
            _reflow.CopyMetadata(row, column, provenance, ref sourceRun, start, count);
        }

        internal GhosttySnapshotPageStorage Storage(TerminalRow row)
        {
            TerminalRowBuffer rows = new();
            foreach (TerminalRow item in Destination) rows.Add(item);
            return _tracker.ReflowSources(rows, _layout, Owner)[row.SnapshotAllocation!];
        }

        private int Link(string uri, uint id) => Owner.RegisterHyperlink(Encoding.ASCII.GetBytes(uri), default, id);

        private GhosttySnapshotPageStorage Seed(TerminalRow row)
        {
            GhosttySnapshotPageStorage storage = new(row.SnapshotAllocation!.Capacity);
            for (int column = 0; column < row.Columns; column++)
            {
                TerminalCell cell = row.ReadOnlyCells[column];
                Assert.Equal(GhosttySnapshotGraphemeAddResult.Success, storage.Graphemes.Set(column, cell.Grapheme is null ? 0 : cell.Grapheme.Length - 1));
                if (Owner.TryGetHyperlink(cell.HyperlinkId, out TerminalHyperlink? link))
                    Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.Hyperlinks.ObserveCell(column, link!.SnapshotEncoding));
                Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.Styles.ChangeCell(column, GhosttySnapshotLivePage.EncodeStyle(in cell)));
            }
            return storage;
        }
    }

    private static GhosttySnapshotPageCapacity Ceiling(GhosttySnapshotCapacityDimension dimension, GhosttySnapshotAllocation layout)
    {
        for (int columns = ushort.MaxValue; columns >= 32768; columns--)
        {
            int rows = (int)(uint.MaxValue / (8UL * ((ulong)columns + 1)));
            GhosttySnapshotPageCapacity capacity = new((ushort)columns, (ushort)rows,
                dimension == GhosttySnapshotCapacityDimension.Styles ? (ushort)4 : (ushort)16, 192, 1024, 2048);
            while (layout.LayoutBytes(capacity) > uint.MaxValue) capacity = capacity with { Rows = (ushort)(capacity.Rows - 1) };
            if (layout.TryAdjustColumns(capacity, columns, out GhosttySnapshotPageCapacity adjusted) &&
                !layout.TryIncreaseCapacity(adjusted, dimension, 0, 2, out _)) return adjusted;
        }
        throw new InvalidOperationException("No representable growth-limited reflow PAGE fixture was found.");
    }
}
