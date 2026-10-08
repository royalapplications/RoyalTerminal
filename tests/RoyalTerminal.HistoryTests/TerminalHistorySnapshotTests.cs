// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.HistoryTests;

/// <summary>Shared backend contract. Physical rows intentionally differ from clipboard exports:
/// blank rows survive, and no soft-wrap or glyph fixup expands a requested range.
/// Ghostty, Windows Terminal and xterm.js all support direct buffer-row access.</summary>
public sealed class TerminalHistorySnapshotTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RangeAndTailRetainBlankRowsWrittenSpacesAndWraps(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 2, 100));
        processor.Process("abcdeZ\r\n\r\nx  "u8);
        var source = (ITerminalHistorySnapshotSource)processor;
        TerminalHistoryBufferInfo info = Info(source);
        Assert.Equal(4, info.AvailableRange.Count);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(10, 100), out var all));
        Assert.Equal(new[] { "abcde", "Z", "", "x  " }, Text(all!));
        Assert.True(all!.Rows[0].WrapsToNext);
        Assert.False(all.Rows[1].WrapsToNext);
        Assert.False(all.Truncated);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(2, 100), out var tail));
        Assert.Equal(new[] { "", "x  " }, Text(tail!));
        Assert.Equal(TerminalHistoryTruncation.RowLimit, tail!.Truncation);
        Assert.Equal(info.AvailableRange.Start + 2, tail.CapturedRange.Start);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(
            new(1, 100, info, info.AvailableRange), out var prefix));
        Assert.Equal(new[] { "abcde" }, Text(prefix!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BudgetsKeepContiguousCompleteRowsAndChargeSeparators(bool native)
    {
        using IVtProcessor processor = Create(native, new(8, 3));
        processor.Process("one\r\ntwo\r\nthree"u8);
        var source = (ITerminalHistorySnapshotSource)processor;
        var info = Info(source);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(3, 7, info, info.AvailableRange), out var prefix));
        Assert.Equal(new[] { "one", "two" }, Text(prefix!));
        Assert.Equal(TerminalHistoryTruncation.CharacterLimit, prefix!.Truncation);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(3, 9), out var tail));
        Assert.Equal(new[] { "two", "three" }, Text(tail!));
        Assert.Equal(TerminalHistoryStatus.BudgetTooSmall, source.CaptureHistory(new(3, 2), out var failure));
        Assert.Null(failure);
        Assert.Equal(TerminalHistoryStatus.BudgetTooSmall, source.CaptureHistory(new(3, 2, info, info.AvailableRange), out failure));
        Assert.Null(failure);
    }

    [Theory]
    [InlineData(false, "🙂", 2)]
    [InlineData(true, "🙂", 2)]
    [InlineData(false, "e\u0301", 2)]
    [InlineData(true, "e\u0301", 2)]
    [InlineData(false, "👩\u200d💻", 5)]
    [InlineData(true, "👩\u200d💻", 5)]
    public void UnicodeRowsAreAtomic(bool native, string text, int length)
    {
        using IVtProcessor processor = Create(native, new(20, 1));
        processor.Process(Encoding.UTF8.GetBytes(text));
        var source = (ITerminalHistorySnapshotSource)processor;
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(1, length), out var snapshot));
        Assert.Equal(text, snapshot!.Rows[0].Text);
        Assert.Equal(TerminalHistoryStatus.BudgetTooSmall, source.CaptureHistory(new(1, length - 1), out snapshot));
        Assert.Null(snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaginationAndOwnedRowsSurviveAllTerminalChanges(bool native)
    {
        var screen = new TerminalScreen(8, 3, 2);
        IVtProcessor processor = Create(native, screen);
        var source = (ITerminalHistorySnapshotSource)processor;
        processor.Process("one\r\ntwo\r\nthree"u8);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(3, 100), out var snapshot));
        var page = snapshot!.ReadPage(0, 2, 7);
        Assert.Equal(new[] { "one", "two" }, page.Rows.Select(row => row.Text));
        Assert.Equal(2, page.NextOffset);
        Assert.Equal(TerminalHistoryStatus.BudgetTooSmall, snapshot.ReadPage(2, 1, 4).Status);
        processor.Process("\r\nfour\r\nfive\r\nsix\u001b[?1049h"u8);
        Resize(processor, 4, 2);
        processor.Reset();
        processor.Dispose();
        Assert.Equal(TerminalHistoryStatus.Disposed, source.GetHistoryBufferInfo(out _));
        Assert.Equal(TerminalHistoryStatus.Disposed, source.CaptureHistory(new(1, 1), out _));
        Assert.Equal(new[] { "one", "two", "three" }, Text(snapshot));
        page = snapshot.ReadPage(2, 10, 5);
        Assert.Equal(snapshot.SnapshotId, page.SnapshotId);
        Assert.Equal("three", Assert.Single(page.Rows).Text);
        Assert.Null(page.NextOffset);
        Assert.Empty(snapshot.ReadPage(3, 1, 1).Rows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvictedRangesNeverSilentlyRetarget(bool native)
    {
        using IVtProcessor processor = Create(native, new(8, 2, 1));
        var source = (ITerminalHistorySnapshotSource)processor;
        processor.Process("one\r\ntwo\r\nthree"u8);
        var original = Info(source);
        processor.Process("\r\nfour\r\nfive"u8);
        var current = Info(source);
        Assert.Equal(original.LayoutEpoch, current.LayoutEpoch);
        Assert.True(current.AvailableRange.Start > original.AvailableRange.Start);
        Assert.Equal(TerminalHistoryStatus.HistoryEvicted, source.CaptureHistory(
            new(1, 100, original, new(original.AvailableRange.Start, 1)), out _));
        Assert.Equal(TerminalHistoryStatus.RangeUnavailable, source.CaptureHistory(
            new(1, 100, current, new(current.AvailableRange.Start + current.AvailableRange.Count, 1)), out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizeClearResetAndStructuralEditsInvalidateRanges(bool native)
    {
        using IVtProcessor processor = Create(native, new(8, 3, 10));
        var source = (ITerminalHistorySnapshotSource)processor;
        processor.Process("one\r\ntwo\r\nthree\r\nfour"u8);
        Check(() => Resize(processor, 4, 3));
        Check(((ITerminalSessionHistoryController)processor).ClearScrollback);
        Check(processor.Reset);
        Check(() => ((ITerminalSessionHistoryController)processor).PrepareForNewSession(true));
        processor.Process("abc\r\ndef"u8);
        Check(() => processor.Process("\u001b[H\u001b[L"u8));
        Check(() => processor.Process("\u001b[?69h\u001b[2;3s\u001b[1;2H\u001b[L"u8));
        processor.Reset();
        Check(((ITerminalSessionHistoryController)processor).ClearScrollback);
        processor.Process("\u001b[?1049h"u8);
        Assert.True(Info(source).AlternateBuffer);
        Check(() => processor.Process("\u001b[?1049l\u001b[?1049h"u8));
        void Check(Action mutate)
        {
            var before = Info(source);
            mutate();
            Assert.Equal(TerminalHistoryStatus.LayoutChanged, source.CaptureHistory(
                new(1, 100, before, new(before.AvailableRange.Start, 1)), out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlternateBuffersAndPresentationHoldsUseAuthoritativeState(bool native)
    {
        using IVtProcessor processor = Create(native, new(8, 2, 10));
        var source = (ITerminalHistorySnapshotSource)processor;
        processor.Process("primary"u8);
        var primary = Info(source);
        processor.Process("\u001b[?1049h\u001b[Hother"u8);
        var alternate = Info(source);
        Assert.True(alternate.AlternateBuffer);
        Assert.NotEqual(primary.BufferId, alternate.BufferId);
        Assert.Equal(TerminalHistoryStatus.BufferUnavailable, source.CaptureHistory(
            new(1, 100, primary, new(primary.AvailableRange.Start, 1)), out _));
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(10, 100), out var snapshot));
        Assert.Equal(new[] { "other", "" }, Text(snapshot!));
        processor.Process("\u001b[?1049l\u001b[?2026h\rnew"u8);
        Assert.Equal(primary.BufferId, Info(source).BufferId);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(2, 100), out snapshot));
        Assert.Equal("newmary", snapshot!.Rows[0].Text);
        processor.Process("\u001b[?2026l"u8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentSerializedOutputAndCaptureStayCoherent(bool native)
    {
        var screen = new TerminalScreen(8, 2, 10);
        using IVtProcessor processor = Create(native, screen);
        var source = (ITerminalHistorySnapshotSource)processor;
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 100; i++) lock (screen.SyncRoot) processor.Process("abcdefgh\r\n"u8);
        }, TestContext.Current.CancellationToken);
        for (int i = 0; i < 100; i++)
        {
            TerminalHistorySnapshot? snapshot;
            lock (screen.SyncRoot) Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(3, 30), out snapshot));
            foreach (var row in snapshot!.Rows) Assert.True(row.Text is "" or "abcdefgh");
            await Task.Yield();
        }
        await writer;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidArgumentsAndCancellationAreExplicit(bool native)
    {
        using IVtProcessor processor = Create(native, new(8, 2));
        var source = (ITerminalHistorySnapshotSource)processor;
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CaptureHistory(new(0, 1), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CaptureHistory(new(1, 0), out _));
        Assert.Throws<ArgumentException>(() => source.CaptureHistory(new(1, 1, Info(source)), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.CaptureHistory(
            new(1, 1, Info(source), new(long.MaxValue, 1)), out _));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => source.CaptureHistory(new(1, 1), out _, cancellation.Token));
        source.CaptureHistory(new(1, 1), out var snapshot);
        Assert.Throws<OperationCanceledException>(() => snapshot!.ReadPage(0, 1, 1, cancellation.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => snapshot!.ReadPage(2, 1, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsLeaveViewportCursorSelectionParserAndCallbacksUnchanged(bool native)
    {
        var screen = new TerminalScreen(8, 2, 100);
        using IVtProcessor processor = Create(native, screen);
        processor.Process("one\r\ntwo\r\nthree\r\nfour"u8);
        if (processor is ITerminalViewportScrollSource scroll) scroll.SetViewportOffsetRows(0);
        else screen.ScrollOffset = 2;
        int offset = screen.ScrollOffset;
        var viewport = (processor as ITerminalViewportScrollSource)?.ViewportScrollState;
        var selection = new TerminalSelectionRange(0, 0, 7, 1);
        string? selectionBefore = ((ITerminalSelectionExportSource)processor).ReadSelection(selection);
        string before = Export(processor);
        int callbacks = 0;
        processor.ResponseCallback = _ => callbacks++;
        // Leave the parser inside CSI. A read must neither finish nor disturb it.
        processor.Process("\u001b[6"u8);
        var source = (ITerminalHistorySnapshotSource)processor;
        var info = Info(source);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(2, 100), out _));
        Assert.Equal(before, Export(processor));
        Assert.Equal(selectionBefore, ((ITerminalSelectionExportSource)processor).ReadSelection(selection));
        Assert.Equal(offset, screen.ScrollOffset);
        Assert.Equal(viewport, (processor as ITerminalViewportScrollSource)?.ViewportScrollState);
        Assert.Equal(info, Info(source));
        Assert.Equal(0, callbacks);
        processor.Process("n"u8);
        Assert.Equal(1, callbacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WideSpacersDoNotExpandRangeAndEmptyRowsCostOnlySeparators(bool native)
    {
        using IVtProcessor processor = Create(native, new(5, 3));
        processor.Process(Encoding.UTF8.GetBytes("abcd界"));
        var source = (ITerminalHistorySnapshotSource)processor;
        var info = Info(source);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(1, 4, info,
            new(info.AvailableRange.Start, 1)), out var head));
        Assert.Equal("abcd", Assert.Single(head!.Rows).Text);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(1, 1, info,
            new(info.AvailableRange.Start + 1, 1)), out var wide));
        Assert.Equal("界", Assert.Single(wide!.Rows).Text);
        processor.Reset();
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(3, 1), out var blank));
        Assert.Equal(2, blank!.Rows.Length);
        Assert.All(blank.Rows, row => Assert.Empty(row.Text));
        Assert.Equal(TerminalHistoryTruncation.CharacterLimit, blank.Truncation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallCaptureAllocationsDoNotGrowWithHistory(bool native)
    {
        long small = Measure(50);
        long large = Measure(5000);
        output.WriteLine($"20 bounded four-row captures: small={small} bytes; large={large} bytes.");
        Assert.InRange(large, 1, small + 4096);

        long Measure(int historyRows)
        {
            using IVtProcessor processor = Create(native, new(80, 2, 6000));
            byte[] row = Encoding.ASCII.GetBytes(new string('a', 79) + "\r\n");
            for (int i = 0; i < historyRows; i++) processor.Process(row);
            var source = (ITerminalHistorySnapshotSource)processor;
            for (int i = 0; i < 10; i++) source.CaptureHistory(new(4, 400), out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(4, 400), out _));
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerializedLifecycleRacesPreserveOwnedPages(bool native)
    {
        var screen = new TerminalScreen(8, 2, 1);
        using IVtProcessor processor = Create(native, screen);
        var source = (ITerminalHistorySnapshotSource)processor;
        processor.Process("retained"u8);
        source.CaptureHistory(new(2, 100), out var owned);
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 40; i++)
            {
                lock (screen.SyncRoot)
                {
                    processor.Process("\r\nabcdefgh\r\nabcdefgh"u8);
                    if (i % 4 == 0) Resize(processor, i % 8 == 0 ? 9 : 8, 2);
                    if (i % 5 == 0) ((ITerminalSessionHistoryController)processor).ClearScrollback();
                    if (i % 7 == 0) processor.Reset();
                }
            }
            processor.Dispose(); // Disposal itself takes the owning lock.
        }, TestContext.Current.CancellationToken);
        for (int i = 0; i < 100; i++)
        {
            lock (screen.SyncRoot)
            {
                var status = source.CaptureHistory(new(3, 100), out var current);
                Assert.True(status is TerminalHistoryStatus.Success or TerminalHistoryStatus.Disposed);
                if (status == TerminalHistoryStatus.Success) Assert.NotNull(current);
                else Assert.Null(current);
            }
            Assert.Equal("retained", owned!.ReadPage(0, 1, 8).Rows[0].Text);
            await Task.Yield();
        }
        await writer;
        Assert.Equal(TerminalHistoryStatus.Disposed, source.CaptureHistory(new(1, 1), out _));
    }

    [Theory]
    [InlineData(false, "e")]
    [InlineData(true, "e")]
    [InlineData(false, "🙂")]
    [InlineData(true, "🙂")]
    public void LargeGraphemesStayAtomicAndBounded(bool native, string prefix)
    {
        using IVtProcessor processor = Create(native, new(20, 1));
        string text = prefix + new string('\u0301', 40);
        processor.Process(Encoding.UTF8.GetBytes(text));
        var source = (ITerminalHistorySnapshotSource)processor;
        Assert.Equal(TerminalHistoryStatus.BudgetTooSmall, source.CaptureHistory(new(1, text.Length - 1), out _));
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(1, text.Length), out var snapshot));
        Assert.Equal(text, snapshot!.Rows[0].Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongRowsRespectExactLimitsAndPreserveInteriorErasedCells(bool native)
    {
        using IVtProcessor processor = Create(native, new(700, 1));
        var source = (ITerminalHistorySnapshotSource)processor;
        string text = new('a', 700);
        processor.Process(Encoding.ASCII.GetBytes(text));
        Assert.Equal(TerminalHistoryStatus.BudgetTooSmall, source.CaptureHistory(new(1, 600), out _));
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(1, 700), out var snapshot));
        Assert.Equal(text, snapshot!.Rows[0].Text);
        processor.Reset();
        processor.Process("\u001b[500Gx"u8);
        Assert.Equal(TerminalHistoryStatus.Success, source.CaptureHistory(new(1, 500), out snapshot));
        Assert.Equal(new string(' ', 499) + "x", snapshot!.Rows[0].Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PhysicalPrefixPruningKeepsEpochAndMonotonicOrigin(bool native)
    {
        using IVtProcessor processor = Create(native, new(8, 2, 1));
        var source = (ITerminalHistorySnapshotSource)processor;
        var before = Info(source);
        // Enough rows to force actual native page pruning, beyond host-visible slack.
        for (int i = 0; i < 5000; i++) processor.Process("abcdefgh\r\n"u8);
        var after = Info(source);
        Assert.Equal(before.LayoutEpoch, after.LayoutEpoch);
        Assert.Equal(5000 - 2, after.AvailableRange.Start);
        Assert.Equal(3, after.AvailableRange.Count);
        Assert.Equal(TerminalHistoryStatus.HistoryEvicted, source.CaptureHistory(
            new(1, 100, before, new(before.AvailableRange.Start, 1)), out _));
    }

    private static string Export(IVtProcessor processor)
    {
        Assert.True(((ITerminalSnapshotExportSource)processor).TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(Extras: new(IncludeCursor: true, IncludeStyle: true, IncludeModes: true)), out string text));
        return text;
    }

    private static TerminalHistoryBufferInfo Info(ITerminalHistorySnapshotSource source)
    {
        Assert.Equal(TerminalHistoryStatus.Success, source.GetHistoryBufferInfo(out var info));
        return Assert.IsType<TerminalHistoryBufferInfo>(info);
    }

    private static string[] Text(TerminalHistorySnapshot snapshot) => snapshot.Rows.Select(row => row.Text).ToArray();

    private static void Resize(IVtProcessor processor, int columns, int rows)
    {
        if (processor is BasicVtProcessor managed) managed.ResizeScreen(columns, rows, 0, 0);
        else processor.NotifyResize(columns, rows);
    }

    private static IVtProcessor Create(bool native, TerminalScreen screen)
    {
        if (!native) return new BasicVtProcessor(screen);
        Assert.True(GhosttyVtProcessor.IsAvailable(), "Build the native library before running history acceptance tests.");
        return new GhosttyVtProcessor(screen);
    }
}
