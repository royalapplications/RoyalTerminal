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

// Ghostty Screen.resize detaches the cursor while holding temporary style/link
// references, clears prompts, restores style then link, and releases temporary
// IDs only if the allocation survived. Every resize reissues implicit link IDs.
// WT TextBuffer::Reflow and xterm.js Buffer.resize are cursor/layout references;
// their global hyperlink identities do not define Ghostty PAGE pressure.
public sealed class ManagedSnapshotCursorResizeTests
{
    private static GhosttySnapshotStyle Bold => new(default, default, default, 1);
    private static GhosttySnapshotStyle Italic => new(default, default, default, 2);
    private const string Close = "\u001b]8;;\u001b\\";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeightOnlyResizeRestartsTheLinkAndReleasesTemporaryReferences(bool explicitId)
    {
        TerminalScreen screen = Screen();
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[1m" + Open("u", explicitId ? "id" : null));
        TerminalRow row = screen.GetViewportRow(0);
        GhosttySnapshotPageAllocation allocation = row.SnapshotAllocation!;
        int original = screen.SnapshotCursorHyperlinkToken(0, 0);
        TerminalScreen retained = screen.CreateStateCopy();
        processor.ResizeScreen(8, 3, 0, 0);
        Assert.Same(allocation, row.SnapshotAllocation);
        Assert.True(screen.SnapshotCursorStyleIsCurrent(0, processor.CursorRow, Bold));
        int current = screen.SnapshotCursorHyperlinkToken(0, 0);
        Assert.Equal(explicitId, original == current);
        AssertLink(screen, current, "u", explicitId ? 0U : 1U);
        Assert.Equal((1, 1UL, 0UL, 64UL), Usage(screen, row));
        Assert.Equal((1, 1UL, 0UL, explicitId ? 64UL : 32UL), Usage(retained, retained.GetViewportRow(0)));
        Process(processor, Close + "\u001b[0m");
        Assert.Equal((0, 0UL, 0UL, 64UL), Usage(screen, row));
    }

    [Fact]
    public void ReflowRestoresTheUnprintedCursorBeforeAnyFurtherInput()
    {
        TerminalScreen screen = Screen();
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[1m" + Open("u"));
        GhosttySnapshotPageAllocation previous = screen.GetViewportRow(0).SnapshotAllocation!;
        processor.ResizeScreen(4, 2, 0, 0);
        TerminalRow row = screen.GetViewportRow(processor.CursorRow);
        Assert.NotSame(previous, row.SnapshotAllocation);
        Assert.Equal((1, 1UL, 0UL, 32UL), Usage(screen, row));
        int token = screen.SnapshotCursorHyperlinkToken(0, 0);
        AssertLink(screen, token, "u", 1);
        AssertCursor(processor, 0, Bold, "u", 1, 2);
        Process(processor, "X");
        Assert.Equal(token, screen.GetViewportRow(processor.CursorRow).ReadOnlyCells[0].HyperlinkId);
        Assert.Equal((1, 1UL, 1UL, 32UL), Usage(screen, row));
    }

    [Fact]
    public void ReservedWidthReuseKeepsTheTemporaryReferenceUntilReinsertion()
    {
        TerminalScreen screen = Screen(reservedColumns: 32);
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[1m" + Open("u"));
        TerminalRow row = screen.GetViewportRow(0);
        GhosttySnapshotPageAllocation previous = row.SnapshotAllocation!;
        TerminalScreen retained = screen.CreateStateCopy();
        processor.ResizeScreen(16, 2, 0, 0, reflowOnResize: false);
        Assert.Same(previous, row.SnapshotAllocation);
        Assert.Equal((1, 1UL, 0UL, 64UL), Usage(screen, row));
        Assert.Equal((1, 1UL, 0UL, 32UL), Usage(retained, retained.GetViewportRow(0)));
        Process(processor, Close + "\u001b[0m");
        Assert.Equal((0, 0UL, 0UL, 64UL), Usage(screen, row));
    }

    [Fact]
    public void ReinsertionGrowthDoesNotReleaseOldNumericIdsInTheReplacementTable()
    {
        TerminalScreen screen = Screen();
        using BasicVtProcessor processor = new(screen);
        string uri = new('u', 1984);
        Process(processor, "\u001b[1m" + Open(uri, "i"));
        TerminalRow row = screen.GetViewportRow(0);
        GhosttySnapshotPageAllocation previous = row.SnapshotAllocation!;
        TerminalScreen retained = screen.CreateStateCopy();
        processor.ResizeScreen(8, 3, 0, 0);
        Assert.NotSame(previous, row.SnapshotAllocation);
        Assert.Equal(4096U, row.SnapshotAllocation!.Capacity.StringBytes);
        Assert.Equal((1, 1UL, 0UL, 2016UL), Usage(screen, row));
        AssertCursor(processor, 0, Bold, uri, 0, 0);
        Process(processor, "A" + Close + "\u001b[0m");
        Assert.Equal((1, 1UL, 1UL, 2016UL), Usage(screen, row));
        Process(processor, "\u001b[1;1H\u001b[X");
        Assert.Equal((0, 0UL, 0UL, 2016UL), Usage(screen, row));
        Assert.Equal((1, 1UL, 0UL, 2016UL), Usage(retained, retained.GetViewportRow(0)));
        Assert.Equal(2048U, retained.GetViewportRow(0).SnapshotAllocation!.Capacity.StringBytes);
    }

    [Fact]
    public void PromptClearingPrecedesCursorRestorationWithoutLosingRetainedValues()
    {
        TerminalScreen screen = Screen();
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b]133;A;redraw=last\a\u001b[1m" + Open("u") + "P");
        TerminalScreen retained = screen.CreateStateCopy();
        processor.ResizeScreen(8, 3, 0, 0);
        TerminalRow row = screen.GetViewportRow(processor.CursorRow);
        foreach (ref readonly TerminalCell cell in row.ReadOnlyCells)
        {
            Assert.Equal(0, cell.Codepoint);
            Assert.Equal(0, cell.HyperlinkId);
            Assert.Equal(default, GhosttySnapshotLivePage.EncodeStyle(in cell));
        }
        Assert.Equal((1, 1UL, 0UL, 64UL), Usage(screen, row));
        AssertCursor(processor, 0, Bold, "u", 1, 2);
        Assert.Equal('P', retained.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal((1, 1UL, 1UL, 32UL), Usage(retained, retained.GetViewportRow(0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoReflowColumnTruncationPrecedesBlankBottomRowTrimming(bool alternate)
    {
        TerminalScreen screen = new(8, 3) { SnapshotScrollbackQuota = new() { PageAlignment = 4096 } };
        using BasicVtProcessor processor = new(screen);
        if (alternate) Process(processor, "\u001b[?47h");
        Process(processor, "TOP\u001b[2;1HKEEP\u001b[3;8H\u001b[1m" + Open("u") + "Z" + Close + "\u001b[0m\u001b[1;1H");
        TerminalScreen retained = screen.CreateStateCopy();
        // An alternate screen disables reflow even when the caller requests it.
        processor.ResizeScreen(4, 2, 0, 0, reflowOnResize: alternate);
        Assert.Equal(2, screen.TotalRows);
        Assert.Equal('T', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal('K', screen.GetViewportRow(1).ReadOnlyCells[0].Codepoint);
        Assert.Equal(0, processor.CursorRow);
        Assert.Equal((0, 0UL, 0UL, 32UL), Usage(screen, screen.GetViewportRow(0), alternate ? 1 : 0));
        Assert.Equal(3, retained.TotalRows);
        Assert.Equal('Z', retained.GetViewportRow(2).ReadOnlyCells[7].Codepoint);
        Assert.Equal((1, 1UL, 1UL, 32UL), Usage(retained, retained.GetViewportRow(0), alternate ? 1 : 0));
    }

    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(false, 16)]
    [InlineData(true, 16)]
    public void ActiveAndDormantBuffersKeepIndependentPensLinksAndCounters(bool alternateVisible, int columns)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(BothCursorSnapshot(alternateVisible));
        TerminalScreen retained = terminal.Screen.CreateStateCopy();
        terminal.Processor.ResizeScreen(columns, 3, 0, 0);
        Assert.Equal(alternateVisible, terminal.Screen.AlternateBufferActive);
        AssertCursor(terminal.Processor, 0, Bold, "p", 100, 101);
        AssertCursor(terminal.Processor, 1, Italic, "a", 200, 201);
        foreach (int key in new[] { 0, 1 })
        {
            Assert.True(terminal.Screen.SnapshotCursorStyleIsCurrent(key, 0, key == 0 ? Bold : Italic));
            TerminalRow row = terminal.Screen.GetSnapshotRows(key)![0];
            Assert.Equal((1, 1UL, 0UL, columns == 8 ? 64UL : 32UL), Usage(terminal.Screen, row, key));
            Assert.Equal((1, 1UL, 0UL, 32UL), Usage(retained, retained.GetSnapshotRows(key)![0], key));
        }
        Process(terminal.Processor, "X");
        int activeKey = alternateVisible ? 1 : 0;
        TerminalRow active = terminal.Screen.GetViewportRow(terminal.Processor.CursorRow);
        AssertLink(terminal.Screen, active.ReadOnlyCells[0].HyperlinkId, alternateVisible ? "a" : "p", alternateVisible ? 200U : 100U);
        Assert.Equal(1UL, Usage(terminal.Screen, active, activeKey).Cells);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImplicitResizeIdentityAlsoAppliesWithoutQuotaTracking(bool tracked)
    {
        TerminalScreen screen = tracked ? Screen() : new(8, 2);
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u"));
        processor.ResizeScreen(8, 3, 0, 0);
        AssertCursor(processor, 0, default, "u", 1, 2);
        processor.ResizeScreen(8, 3, 0, 0); // A true no-op must not restart it.
        AssertCursor(processor, 0, default, "u", 1, 2);
        Process(processor, "A");
        AssertLink(screen, screen.GetViewportRow(processor.CursorRow).ReadOnlyCells[0].HyperlinkId, "u", 1);
    }

    [Fact]
    public void AbortedResizeRestoresExactReferencesWithoutGrowthOrCounterConsumption()
    {
        TerminalScreen screen = Screen();
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b[1m" + Open(new string('u', 1984), "i"));
        TerminalRow row = screen.GetViewportRow(0);
        object cells = row.SearchStorageIdentity;
        GhosttySnapshotPageAllocation page = row.SnapshotAllocation!;
        int token = screen.SnapshotCursorHyperlinkToken(0, 0), original = token;
        uint counter = 7;
        TerminalScreen retained = screen.CreateStateCopy();
        GhosttySnapshotPageTracker.CursorResizeLease? lease = screen.BeginSnapshotCursorResize(0, 0, Bold, ref token, ref counter);
        Assert.NotNull(lease);
        Assert.False(screen.SnapshotCursorStyleIsCurrent(0, 0, Bold));
        Assert.Equal(0, screen.SnapshotCursorHyperlinkToken(0, -1));
        Assert.Equal((1, 1UL, 0UL, 2016UL), Usage(screen, row));
        lease!.Dispose(); lease.Dispose();
        Assert.True(screen.SnapshotCursorStyleIsCurrent(0, 0, Bold));
        Assert.Equal(original, screen.SnapshotCursorHyperlinkToken(0, 0));
        Assert.Equal(7U, counter);
        Assert.Same(cells, row.SearchStorageIdentity);
        Assert.Same(page, row.SnapshotAllocation);
        Assert.Equal((1, 1UL, 0UL, 2016UL), Usage(screen, row));
        Assert.Equal((1, 1UL, 0UL, 2016UL), Usage(retained, retained.GetViewportRow(0)));
    }

    [Fact]
    public void SuspendedReferencesSurviveCopiesButAreExcludedFromPageRebuilds()
    {
        GhosttySnapshotPageCapacity capacity = new(8, 2, 8, 192, 0, 2048);
        GhosttySnapshotPageStorage storage = new(capacity);
        Assert.Equal(GhosttySnapshotSetAddResult.Success, storage.Styles.ChangeCursor(Bold));
        byte[] link = new TerminalHyperlink("u"u8, default, 0).SnapshotEncoding;
        Assert.Equal(GhosttySnapshotHyperlinkAddResult.Success, storage.Hyperlinks.StartCursor(link));
        int style = storage.Styles.SuspendCursorReference(), hyperlink = storage.Hyperlinks.SuspendCursorReference();
        GhosttySnapshotPageStorage copy = storage.Copy();
        Assert.Same(storage.AllocationIdentity, copy.AllocationIdentity);
        Assert.Equal(1, copy.Styles.Count);
        Assert.Equal(1, copy.Hyperlinks.Count);
        Assert.Equal(default, copy.Styles.Cursor);
        Assert.Equal(0, copy.Hyperlinks.CursorId);
        Assert.True(copy.Rebuild(capacity, restoreCursor: true, out GhosttySnapshotPageStorage? rebuilt));
        Assert.NotSame(copy.AllocationIdentity, rebuilt!.AllocationIdentity);
        Assert.Equal(0, rebuilt.Styles.Count);
        Assert.Equal(0, rebuilt.Hyperlinks.Count);
        Assert.Equal(0UL, rebuilt.Hyperlinks.StringBytes);
        copy.Styles.RestoreCursorReference(style); copy.Hyperlinks.RestoreCursorReference(hyperlink);
        Assert.Equal(Bold, copy.Styles.Cursor);
        Assert.Equal(hyperlink, copy.Hyperlinks.CursorId);
        copy.Styles.ChangeCursor(default); copy.Hyperlinks.EndCursor();
        Assert.Equal(0, copy.Styles.Count);
        Assert.Equal(0, copy.Hyperlinks.Count);
        Assert.Equal(1, storage.Styles.Count);
        Assert.Equal(1, storage.Hyperlinks.Count);
        storage.Styles.ReleaseTableReference(style); storage.Hyperlinks.ReleaseTableReference(hyperlink);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedResizeCursorStateAndCapacityMatchNativeForBothBuffers(bool alternateVisible)
    {
        RequireNative();
        byte[] snapshot = BothCursorSnapshot(alternateVisible);
        using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
        using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
        foreach ((ushort columns, ushort rows) in new (ushort, ushort)[] { (8, 3), (16, 3), (8, 2), (8, 4), (4, 2) })
        {
            native.Resize(columns, rows);
            managed.Processor.ResizeScreen(columns, rows, 0, 0);
            using GhosttySnapshotStateReader expectedReader = new(GhosttySnapshot.Encode(native), new());
            using GhosttySnapshotStateReader actualReader = new(managed.Processor.GetBinarySnapshot(), new());
            GhosttySnapshotReadyState expected = expectedReader.ReadReady(), actual = actualReader.ReadReady();
            foreach (GhosttySnapshotScreen reference in expected.Screens)
            {
                GhosttySnapshotScreenState state = FindScreen(actual, reference.State.Key).State;
                Assert.Equal(reference.State.Pen, state.Pen);
                Assert.Equal(reference.State.CursorX, state.CursorX);
                Assert.Equal(reference.State.CursorY, state.CursorY);
                Assert.Equal(reference.State.HyperlinkImplicitCounter, state.HyperlinkImplicitCounter);
                Assert.True(reference.State.TryGetHyperlink(out GhosttySnapshotHyperlink expectedLink));
                Assert.True(state.TryGetHyperlink(out GhosttySnapshotHyperlink actualLink));
                Assert.Equal(expectedLink.ImplicitId, actualLink.ImplicitId);
                Assert.True(expectedLink.Uri.SequenceEqual(actualLink.Uri));
                TerminalRowBuffer buffer = managed.Screen.GetSnapshotRows(state.Key)!;
                TerminalRow cursor = buffer[buffer.Count - rows + state.CursorY];
                GhosttySnapshotPageCapacity expectedCapacity = CursorPage(reference, rows).Capacity;
                Assert.Equal(expectedCapacity.Styles, cursor.SnapshotAllocation!.Capacity.Styles);
                Assert.Equal(expectedCapacity.HyperlinkBytes, cursor.SnapshotAllocation.Capacity.HyperlinkBytes);
                Assert.Equal(expectedCapacity.StringBytes, cursor.SnapshotAllocation.Capacity.StringBytes);
            }
        }
    }

    private static TerminalScreen Screen(int reservedColumns = 8)
    {
        TerminalScreen owner = new(8, 2);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(8, 2, 10000, owner.Theme);
        GhosttySnapshotPageAllocation page = new(new((ushort)reservedColumns, 8, 8, 192, 0, 2048));
        screen.InstallSnapshotRows([new(8) { SnapshotAllocation = page }, new(8) { SnapshotAllocation = page, SnapshotAllocationRow = 1 }], null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        return screen;
    }

    private static (int Styles, ulong Links, ulong Cells, ulong Bytes) Usage(TerminalScreen screen, TerminalRow row, int key = 0)
    {
        List<TerminalRow> group = [];
        foreach (TerminalRow member in screen.GetSnapshotRows(key)!)
            if (ReferenceEquals(row.SnapshotAllocation, member.SnapshotAllocation)) group.Add(member);
        Assert.True(screen.TryGetSnapshotStyleUsage(row.SnapshotAllocation!, group, out int styles));
        Assert.True(screen.TryGetSnapshotHyperlinkUsage(row.SnapshotAllocation!, group, out ulong links, out ulong cells, out ulong bytes));
        return (styles, links, cells, bytes);
    }

    private static void AssertLink(TerminalScreen screen, int token, string uri, uint implicitId)
    {
        Assert.NotEqual(0, token);
        Assert.True(screen.TryGetHyperlink(token, out TerminalHyperlink? link));
        Assert.Equal(uri, link!.Uri);
        Assert.Equal(implicitId, link.ImplicitId);
    }

    private static void AssertCursor(BasicVtProcessor processor, int key, GhosttySnapshotStyle pen, string uri, uint implicitId, uint counter)
    {
        using GhosttySnapshotStateReader reader = new(processor.GetBinarySnapshot(), new());
        GhosttySnapshotScreenState state = FindScreen(reader.ReadReady(), key).State;
        Assert.Equal(pen, state.Pen);
        Assert.Equal(counter, state.HyperlinkImplicitCounter);
        Assert.True(state.TryGetHyperlink(out GhosttySnapshotHyperlink link));
        Assert.Equal(uri, Encoding.UTF8.GetString(link.Uri));
        Assert.Equal(implicitId, link.ImplicitId);
    }

    private static GhosttySnapshotScreen FindScreen(GhosttySnapshotReadyState ready, int key)
    {
        foreach (GhosttySnapshotScreen screen in ready.Screens) if (screen.State.Key == key) return screen;
        throw new InvalidOperationException("Missing screen.");
    }

    private static GhosttySnapshotPage CursorPage(GhosttySnapshotScreen screen, int viewportRows)
    {
        int count = 0;
        foreach (GhosttySnapshotPage page in screen.Pages) count += page.Grid.Rows;
        int row = count - viewportRows + screen.State.CursorY;
        foreach (GhosttySnapshotPage page in screen.Pages)
        {
            if (row < page.Grid.Rows) return page;
            row -= page.Grid.Rows;
        }
        throw new InvalidOperationException("Missing cursor page.");
    }

    private static byte[] BothCursorSnapshot(bool alternateVisible)
    {
        using BasicVtProcessor source = new(new TerminalScreen(8, 2));
        Process(source, "\u001b[?47h");
        if (!alternateVisible) Process(source, "\u001b[?47l");
        using GhosttySnapshotRecordReader reader = new(source.GetBinarySnapshot(), 16 * 1024 * 1024);
        reader.ReadEnvelope();
        List<SnapshotTestRecord> records = [];
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            byte[] bytes = payload.ToArray();
            if (tag == GhosttySnapshotRecordTag.Screen)
            {
                bool primary = BinaryPrimitives.ReadUInt16LittleEndian(bytes) == 0;
                (primary ? Bold : Italic).Write(bytes.AsSpan(18));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(34), primary ? 100U : 200U);
                int offset = GhosttySnapshotScreenState.HeaderLength + (bytes[52] == 0 ? 0 : GhosttySnapshotSavedCursor.Length);
                using MemoryStream output = new();
                output.Write(bytes.AsSpan(0, offset));
                new GhosttySnapshotHyperlink(false, primary ? 50U : 60U, default, primary ? "p"u8 : "a"u8).WriteTo(output);
                bytes = output.ToArray();
            }
            records.Add(new(tag, bytes));
            if (tag == GhosttySnapshotRecordTag.Finish) return SnapshotTestRecords.Encode(records);
        }
    }

    private static string Open(string uri, string? id = null) => $"\u001b]8;{(id is null ? "" : "id=" + id)};{uri}\u001b\\";
    private static void Process(BasicVtProcessor processor, string input) => processor.Process(Encoding.UTF8.GetBytes(input));
    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable()) return;
        Assert.False(Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS") == "1", "Required native VT library is unavailable.");
        Assert.Skip("Native VT library is unavailable.");
    }
}
