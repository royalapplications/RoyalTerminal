// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty Screen.startHyperlink/cursorChangePin/cursorSetHyperlink and
// Page.clonePartialRowFrom define page pressure, including dead strings and
// temporary URI allocations. WT TextBuffer::_PruneHyperlinks and xterm.js
// OscLinkService release retired row/line ownership, but their global registries
// are not per-page allocator oracles. Keep host tokens separate from native IDs.
public sealed class ManagedSnapshotHyperlinkLifecycleTests
{
    [Fact]
    public void UnprintedCursorGrowsBothTablesAndCloseRetainsDeadStrings()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 0, 0, 0));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u", "id"));
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal((ushort)192, row.SnapshotAllocation!.Capacity.HyperlinkBytes);
        Assert.Equal(2048U, row.SnapshotAllocation.Capacity.StringBytes);
        Assert.Equal((1UL, 0UL, 64UL), Usage(screen, row));
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, Close);
        Assert.Equal((0UL, 0UL, 64UL), Usage(screen, row));
        Assert.Equal((1UL, 0UL, 64UL), Usage(retained, retained.GetViewportRow(0)));
        Process(processor, "A");
        Assert.Equal(0, row.ReadOnlyCells[0].HyperlinkId);
    }

    [Fact]
    public void ErasingCellsDoesNotReleaseTheIndependentCursor()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u") + "AB\u001b[1;1H\u001b[2X");
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal((1UL, 0UL, 32UL), Usage(screen, row));
        Process(processor, "C" + Close);
        Assert.Equal((1UL, 1UL, 32UL), Usage(screen, row));
        Process(processor, "\u001b[1;1HD");
        Assert.Equal((0UL, 0UL, 32UL), Usage(screen, row));
        Assert.Equal(0, row.ReadOnlyCells[0].HyperlinkId);
    }

    [Fact]
    public void ReopeningAnEqualExplicitLinkStillRequiresDuplicateStringScratch()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        string open = Open(new string('u', 1984), "i");
        Process(processor, open + "A");
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, open + "B");
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(4096U, row.SnapshotAllocation!.Capacity.StringBytes);
        Assert.Equal((1UL, 2UL, 2016UL), Usage(screen, row));
        Assert.Equal(row.ReadOnlyCells[0].HyperlinkId, row.ReadOnlyCells[1].HyperlinkId);
        Assert.Equal(2048U, retained.GetViewportRow(0).SnapshotAllocation!.Capacity.StringBytes);
        Assert.Equal((1UL, 1UL, 2016UL), Usage(retained, retained.GetViewportRow(0)));
    }

    [Fact]
    public void StyleGrowthCanDropTheCursorButKeepsLinkedCellsAndCowReaders()
    {
        TerminalScreen screen = Screen(new(8, 2, 4, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open(new string('u', 1984), "i") + "\u001b[1mA\u001b[0;3mB");
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, "\u001b[0;2mC");
        TerminalRow row = screen.GetViewportRow(0);
        Assert.True(row.SnapshotAllocation!.Capacity.Styles > 4);
        Assert.Equal(2048U, row.SnapshotAllocation.Capacity.StringBytes);
        Assert.Equal(0, row.ReadOnlyCells[2].HyperlinkId);
        Assert.NotEqual(0, row.ReadOnlyCells[0].HyperlinkId);
        Assert.Equal((1UL, 2UL, 2016UL), Usage(screen, row));
        Assert.NotEqual(0, retained.SnapshotCursorHyperlinkToken(0, 0));
        using GhosttySnapshotStateReader reader = new(processor.GetBinarySnapshot(), new());
        Assert.False(reader.ReadReady().Screens[0].State.TryGetHyperlink(out _));
    }

    [Fact]
    public void MapGrowthReservesUriScratchAndPreservesTheCursorIdentity()
    {
        TerminalScreen screen = Screen(new(120, 2, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open(new string('u', 1536)) + new string('A', 102));
        TerminalRow row = screen.GetViewportRow(0);
        int token = row.ReadOnlyCells[0].HyperlinkId;
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, "B");
        Assert.Equal((ushort)384, row.SnapshotAllocation!.Capacity.HyperlinkBytes);
        Assert.Equal(4096U, row.SnapshotAllocation.Capacity.StringBytes);
        Assert.Equal(token, row.ReadOnlyCells[102].HyperlinkId);
        Assert.Equal((1UL, 103UL, 1536UL), Usage(screen, row));
        Assert.Equal((1UL, 102UL, 1536UL), Usage(retained, retained.GetViewportRow(0)));
        Assert.Equal((ushort)192, retained.GetViewportRow(0).SnapshotAllocation!.Capacity.HyperlinkBytes);
    }

    [Fact]
    public void UriOnlyMapScratchDoesNotGuaranteeAnExplicitIdCanBeRestored()
    {
        TerminalScreen screen = Screen(new(120, 2, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u", new string('i', 1984)) + new string('A', 103));
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal((ushort)384, row.SnapshotAllocation!.Capacity.HyperlinkBytes);
        Assert.Equal(2048U, row.SnapshotAllocation.Capacity.StringBytes);
        Assert.Equal((1UL, 102UL, 2016UL), Usage(screen, row));
        Assert.Equal(0, row.ReadOnlyCells[102].HyperlinkId);
        Assert.Equal(0, screen.SnapshotCursorHyperlinkToken(0, -1));
    }

    [Fact]
    public void GlyphGrowthRebuildsLinksAndReclaimsDeadStringAllocations()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u") + "A" + Close + "\u001b[1;1HB");
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal((0UL, 0UL, 32UL), Usage(screen, row));
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, "\u0301");
        Assert.Equal((0UL, 0UL, 0UL), Usage(screen, row));
        Assert.Equal((0UL, 0UL, 32UL), Usage(retained, retained.GetViewportRow(0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PageCrossingReissuesOnlyImplicitIdentities(bool explicitId)
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        screen.GetViewportRow(1).SnapshotAllocation = new(new(8, 1, 8, 192, 0, 2048));
        screen.GetViewportRow(1).SnapshotAllocationRow = 0;
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u", explicitId ? "id" : null) + "A\u001b[2;1HB");
        int first = screen.GetViewportRow(0).ReadOnlyCells[0].HyperlinkId;
        int second = screen.GetViewportRow(1).ReadOnlyCells[0].HyperlinkId;
        Assert.Equal(explicitId, first == second);
        Assert.True(screen.TryGetHyperlink(second, out TerminalHyperlink? link));
        Assert.Equal(explicitId ? 0U : 1U, link!.ImplicitId);
        Assert.Equal((1UL, 1UL, explicitId ? 64UL : 32UL), Usage(screen, screen.GetViewportRow(0)));
        Process(processor, Close + "\u001b[2;1H\u001b[X");
        Assert.Equal((0UL, 0UL, explicitId ? 64UL : 32UL), Usage(screen, screen.GetViewportRow(1)));
    }

    [Fact]
    public void SamePageMovementAndSavedCursorRestoreDoNotReinsertTheLink()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open(new string('u', 1984), "i") + "A\u001b7\u001b[2;1HB\u001b8C");
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal(2048U, row.SnapshotAllocation!.Capacity.StringBytes);
        Assert.Equal((1UL, 3UL, 2016UL), Usage(screen, row));
    }

    [Fact]
    public void CharacterShiftsTransferOwnershipAndEraseReleasesOnlyRemovedLinks()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("a") + "A" + Open("b") + "B" + Close + "\u001b[1;1H\u001b[@");
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal((2UL, 2UL, 64UL), Usage(screen, row));
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, "\u001b[P\u001b[P");
        Assert.Equal((1UL, 1UL, 64UL), Usage(screen, row));
        Assert.Equal('B', row.ReadOnlyCells[0].Codepoint);
        Assert.Equal((2UL, 2UL, 64UL), Usage(retained, retained.GetViewportRow(0)));
    }

    [Fact]
    public void CrossPageRowShiftGrowsLinksAndStringsBeforeCloningStyles()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        screen.GetViewportRow(1).SnapshotAllocation = new(new(8, 1, 8, 0, 0, 0));
        screen.GetViewportRow(1).SnapshotAllocationRow = 0;
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u") + "A" + Close + "\u001b[1;1H");
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, "\u001bM");
        Assert.Equal((0UL, 0UL, 32UL), Usage(screen, screen.GetViewportRow(0)));
        Assert.Equal((1UL, 1UL, 32UL), Usage(screen, screen.GetViewportRow(1)));
        Assert.Equal((ushort)192, screen.GetViewportRow(1).SnapshotAllocation!.Capacity.HyperlinkBytes);
        Assert.Equal(2048U, screen.GetViewportRow(1).SnapshotAllocation!.Capacity.StringBytes);
        Assert.Equal((1UL, 1UL, 32UL), Usage(retained, retained.GetViewportRow(0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizeClonesLiveLinksAndTheirUsageWithoutMutatingCowReaders(bool reflow)
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u", "i") + "AB" + Close);
        TerminalScreen retained = screen.CreateStateCopy();
        processor.ResizeScreen(16, 2, 0, 0, reflowOnResize: reflow);
        TerminalRow row = screen.GetSnapshotRows(0)![0];
        Assert.Equal((1UL, 2UL, 64UL), Usage(screen, row));
        using (GhosttySnapshotPageTracker.RowEdit edit = screen.EditSnapshotRowMetadata(row))
        {
            edit.Clear(0, 2);
            row.Cells[..2].Fill(TerminalCell.Empty());
        }
        Assert.Equal((0UL, 0UL, 64UL), Usage(screen, row));
        Assert.Equal((1UL, 2UL, 64UL), Usage(retained, retained.GetViewportRow(0)));
    }

    [Fact]
    public void ScreenRestoreReinstatesAnUnprintedCursorWithoutChangingItsNextImplicitId()
    {
        using BasicVtProcessor source = new(new TerminalScreen(8, 2));
        Process(source, Open("u") + "A" + Open("v"));
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(source.GetBinarySnapshot());
        Process(restored.Processor, "B");
        TerminalRow row = restored.Screen.GetViewportRow(0);
        Assert.Equal((2UL, 2UL, 64UL), Usage(restored.Screen, row));
        Assert.True(restored.Screen.TryGetHyperlink(row.ReadOnlyCells[1].HyperlinkId, out TerminalHyperlink? cursor));
        Assert.Equal(1U, cursor!.ImplicitId);
        Process(restored.Processor, Open("w") + "C");
        Assert.True(restored.Screen.TryGetHyperlink(row.ReadOnlyCells[2].HyperlinkId, out TerminalHyperlink? next));
        Assert.Equal(2U, next!.ImplicitId);
    }

    [Fact]
    public void SoftResetReleasesTheCursorWithoutReclaimingDeadStrings()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u") + "\u001b[!pA");
        Assert.Equal((0UL, 0UL, 32UL), Usage(screen, screen.GetViewportRow(0)));
        Assert.Equal(0, screen.GetViewportRow(0).ReadOnlyCells[0].HyperlinkId);
    }

    [Fact]
    public void HistoryRetirementReleasesCellsButPreservesCurrentLinksAndCowReaders()
    {
        TerminalScreen screen = Screen(new(8, 8, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("a") + "A\r\n" + Open("b") + "B" + Close + "\r\nC");
        TerminalScreen retained = screen.CreateStateCopy();
        TerminalRow row = screen.GetViewportRow(0);
        screen.ScrollbackLimit = 0;
        Assert.Equal((1UL, 1UL, 64UL), Usage(screen, row));
        Assert.Equal((2UL, 2UL, 64UL), Usage(retained, retained.GetSnapshotRows(0)![0]));
    }

    [Fact]
    public void ScreenSwitchEndsCursorOwnershipWithoutUnlinkingOldCells()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u") + "A\u001b[?47h");
        Assert.Equal(0, screen.SnapshotCursorHyperlinkToken(0, -1));
        Process(processor, Open("v") + "B\u001b[?47lC");
        Assert.Equal(0, screen.SnapshotCursorHyperlinkToken(1, -1));
        TerminalRow row = screen.GetViewportRow(0);
        Assert.Equal((1UL, 1UL, 32UL), Usage(screen, row));
        Assert.NotEqual(0, row.ReadOnlyCells[0].HyperlinkId);
        Assert.Equal(0, row.ReadOnlyCells[1].HyperlinkId);
    }

    [Fact]
    public void EncodedIdentityCachePreservesRawBytesAndIsSharedByCowRegistries()
    {
        TerminalScreen screen = Screen(new(8, 2, 8, 192, 0, 2048));
        int token = screen.RegisterHyperlink([0xff, 0x75], [0xfe], 10);
        byte[] encoded = screen.SnapshotHyperlinkEncoding(token)!;
        Assert.Same(encoded, screen.SnapshotHyperlinkEncoding(token));
        Assert.Same(encoded, screen.CreateStateCopy().SnapshotHyperlinkEncoding(token));
        GhosttySnapshotHyperlink link = GhosttySnapshotHyperlink.Read(encoded, out int consumed);
        Assert.Equal(encoded.Length, consumed);
        Assert.True(link.Uri.SequenceEqual(new byte[] { 0xff, 0x75 }));
        Assert.True(link.ExplicitId.SequenceEqual(new byte[] { 0xfe }));
    }

    [Fact]
    public void MutationPressureAndCursorStateMatchNativeContinuation()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
            Assert.Skip("Native VT library is unavailable.");
        }
        TerminalScreen screen = Screen(new(120, 2, 4, 192, 0, 2048));
        using BasicVtProcessor source = new(screen);
        byte[] snapshot = source.GetBinarySnapshot();
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        string[] inputs = [Open(new string('u', 1536)) + new string('A', 103), Close + "\u001b[1;1H\u001b[50X",
            Open("v", "id") + "\u001b[1mB\u001b[0;3mC\u001b[0;2mD", Close + "\u001b[1;1H\u001b[@\u001b[P"];
        foreach (string input in inputs)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            native.Write(bytes); managed.Processor.Process(bytes);
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            GhosttySnapshotScreen expected = reader.ReadReady().Screens[0];
            TerminalRow actual = managed.Screen.GetViewportRow(0);
            GhosttySnapshotPage page = expected.Pages[^1];
            Assert.Equal(page.Capacity.HyperlinkBytes, actual.SnapshotAllocation!.Capacity.HyperlinkBytes);
            Assert.Equal(page.Capacity.StringBytes, actual.SnapshotAllocation.Capacity.StringBytes);
            using GhosttySnapshotStateReader current = new(managed.Processor.GetBinarySnapshot(), new());
            GhosttySnapshotScreenState state = current.ReadReady().Screens[0].State;
            Assert.Equal(expected.State.HyperlinkImplicitCounter, state.HyperlinkImplicitCounter);
            Assert.Equal(expected.State.TryGetHyperlink(out GhosttySnapshotHyperlink expectedLink), state.TryGetHyperlink(out GhosttySnapshotHyperlink actualLink));
            Assert.True(expectedLink.Uri.SequenceEqual(actualLink.Uri));
            Assert.True(expectedLink.ExplicitId.SequenceEqual(actualLink.ExplicitId));
        }
    }

    private const string Close = "\u001b]8;;\u001b\\";
    private static string Open(string uri, string? id = null) => $"\u001b]8;{(id is null ? "" : "id=" + id)};{uri}\u001b\\";
    private static void Process(BasicVtProcessor processor, string input) => processor.Process(Encoding.UTF8.GetBytes(input));

    private static TerminalScreen Screen(GhosttySnapshotPageCapacity capacity)
    {
        TerminalScreen owner = new(capacity.Columns, 2);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(capacity.Columns, 2, 10000, owner.Theme);
        GhosttySnapshotPageAllocation page = new(capacity);
        screen.InstallSnapshotRows([new(capacity.Columns) { SnapshotAllocation = page },
            new(capacity.Columns) { SnapshotAllocation = page, SnapshotAllocationRow = 1 }], null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        return screen;
    }

    private static (ulong Links, ulong Cells, ulong Bytes) Usage(TerminalScreen screen, TerminalRow member)
    {
        List<TerminalRow> group = [];
        foreach (TerminalRow row in screen.GetSnapshotRows(0)!)
            if (ReferenceEquals(row.SnapshotAllocation, member.SnapshotAllocation)) group.Add(row);
        Assert.True(screen.TryGetSnapshotHyperlinkUsage(member.SnapshotAllocation!, group, out ulong links, out ulong cells, out ulong bytes));
        return (links, cells, bytes);
    }
}
