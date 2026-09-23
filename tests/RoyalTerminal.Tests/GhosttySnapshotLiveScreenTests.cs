// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotLiveScreenTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void StagingRetainsBothPhysicalBuffersAndIncidentalHistory(int activeKey)
    {
        using GhosttySnapshotStateReader fixture = new(GhosttySnapshotFramingTests.Fixture("complete-v1.hex"), new());
        GhosttySnapshotReadyState source = fixture.ReadReady();
        using MemoryStream terminalPayload = new(); source.Terminal.WritePayloadTo(terminalPayload);
        byte[] bytes = terminalPayload.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(21), (ushort)activeKey);
        GhosttySnapshotTerminalState terminal = GhosttySnapshotTerminalState.Read(bytes, 100, 1024);
        TerminalScreen owner = new(2, 3);
        TerminalRow narrow = new(1), wide = new(5), normal = new(2);
        narrow[0].Codepoint = 'N'; wide[4].Codepoint = 'W'; normal[0].Codepoint = 'V';
        wide[4].HyperlinkId = owner.RegisterHyperlink([255, 254], [128], 0);
        GhosttySnapshotPage[] pages =
        [
            GhosttySnapshotLivePage.Capture([narrow], owner, 100),
            GhosttySnapshotLivePage.Capture([wide, wide], owner, 100),
            GhosttySnapshotLivePage.Capture([normal], owner, 100),
        ];
        // Deliberately reversed routes: identity comes from the SCREEN key, not array order.
        GhosttySnapshotReadyState ready = new(terminal,
            [new(source.Screens[1].State, pages), new(source.Screens[0].State, pages)], []);
        TerminalScreen staged = GhosttySnapshotLiveScreen.Stage(ready, TerminalTheme.Dark, 0);
        Assert.Equal(activeKey == 1, staged.AlternateBufferActive);
        Assert.Equal((2, 3, 4, 1), (staged.Columns, staged.ViewportRows, staged.TotalRows, staged.ViewportTopAbsoluteRow));
        for (int key = 0; key < 2; key++)
        {
            TerminalRowBuffer rows = staged.GetSnapshotRows(key)!;
            Assert.Equal(4, rows.Count);
            Assert.Equal(new[] { 1, 5, 5, 2 }, Enumerable.Range(0, 4).Select(i => rows[i].Columns));
            Assert.Equal('N', rows[0][0].Codepoint);
            Assert.Equal('W', rows[1][4].Codepoint);
            Assert.True(staged.TryGetHyperlink(rows[1][4].HyperlinkId, out TerminalHyperlink? link));
            Assert.Equal(new byte[] { 255, 254 }, link!.UriBytes.ToArray());
        }
        // No hidden aliasing between two installs or between decoded pages and live cells.
        TerminalScreen second = GhosttySnapshotLiveScreen.Stage(ready, TerminalTheme.Dark, 0);
        staged.GetSnapshotRows(0)![1][4].Codepoint = 'X';
        Assert.Equal('W', second.GetSnapshotRows(0)![1][4].Codepoint);
        Assert.Equal('W', staged.GetSnapshotRows(1)![1][4].Codepoint);
        Assert.Throws<ArgumentOutOfRangeException>(() => staged.GetSnapshotRows(2));
    }

    [Fact]
    public void StagingResolvesSnapshotColorsWithoutReplacingHostPresentationPreferences()
    {
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        byte[] terminal = records[0].Payload;
        // Absent foreground uses host fallback; explicit black background is not absence.
        terminal.AsSpan(63, 24).Clear();
        terminal[63] = 1; terminal[64] = 0xAA; // Default background.
        terminal[67] = 1; // Black override.
        terminal[79] = 1; terminal[80] = 0x12; terminal[81] = 0x34; terminal[82] = 0x56;
        using GhosttySnapshotStateReader reader = new(SnapshotTestRecords.Encode(records), new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        TerminalTheme host = new(0xFFABCDEF, 0xFF010203, 0xFF040506, TerminalTheme.Dark.Palette,
            selectionForeground: 0xFF112233, selectionBackground: 0xFF445566, boldColor: 0xFF778899,
            cursorTextColor: 0xFF998877);
        TerminalScreen staged = GhosttySnapshotLiveScreen.Stage(ready, host, 100);
        Assert.Equal(0xFFABCDEFu, staged.DefaultForeground);
        Assert.Equal(0xFF000000u, staged.DefaultBackground);
        Assert.Equal(0xFF123456u, staged.Theme.CursorColor);
        Assert.Equal(host.CursorTextColor, staged.Theme.CursorTextColor);
        Assert.Equal(host.SelectionForeground, staged.Theme.SelectionForeground);
        Assert.Equal(host.SelectionBackground, staged.Theme.SelectionBackground);
        Assert.Equal(host.BoldColor, staged.Theme.BoldColor);
        for (int index = 0; index < 256; index++)
        {
            Assert.Equal(0xFF000000 | ready.Terminal.CurrentPaletteColor(index), staged.Theme.Palette[index]);
            Assert.Equal(ready.Terminal.HasPaletteOverride(index), staged.Theme.Palette.IsExplicitOverride(index));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeBothScreenCellsSurviveStagingAndRecapture(bool alternate)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native snapshot storage differential available: {available}");
        if (!available) return;
        using GhosttyTerminal native = new(12, 3);
        native.SetScrollbackMaxBytes(4 * 1024 * 1024);
        native.SetContinuationMaxBytes(65536);
        for (int i = 0; i < 100; i++)
            native.Write(Encoding.UTF8.GetBytes($"\u001b[38;5;{i}m{i}:界a\u0301\r\n"));
        native.Write("\u001b]8;id=primary;url\aP\u001b[?47h\u001b]8;id=alternate;url\a\u001b[44mALT"u8);
        if (!alternate) native.Write("\u001b[?47l"u8);
        native.Write("\u001b[31"u8);
        byte[] original = GhosttySnapshot.Encode(native);
        using GhosttySnapshotStateReader reader = new(original, new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        TerminalScreen staged = GhosttySnapshotLiveScreen.Stage(ready, TerminalTheme.Dark, 10000);
        using MemoryStream encoded = new(); encoded.Write(GhosttySnapshotFraming.Envelope);
        using MemoryStream payload = new(); ready.Terminal.WritePayloadTo(payload);
        WritePayload(GhosttySnapshotRecordTag.Terminal);
        foreach (GhosttySnapshotScreen screen in ready.Screens)
        {
            screen.State.WritePayloadTo(payload); WritePayload(GhosttySnapshotRecordTag.Screen);
            TerminalRowBuffer rows = staged.GetSnapshotRows(screen.State.Key)!;
            int offset = 0;
            foreach (GhosttySnapshotPage originalPage in screen.Pages)
            {
                TerminalRow[] pageRows = new TerminalRow[originalPage.Grid.Rows];
                for (int i = 0; i < pageRows.Length; i++) pageRows[i] = rows[offset++];
                GhosttySnapshotLivePage.Capture(pageRows, staged, 1_000_000).WritePayloadTo(payload);
                WritePayload(GhosttySnapshotRecordTag.Page);
            }
            Assert.Equal(rows.Count, offset);
        }
        GhosttySnapshotFraming.WriteRecord(encoded, GhosttySnapshotRecordTag.Continuation, ready.Continuation);
        GhosttySnapshotFraming.WriteRecord(encoded, GhosttySnapshotRecordTag.Ready, []);
        encoded.Write(original.AsSpan((int)reader.SourceOffset));
        using GhosttyTerminal direct = GhosttySnapshot.Decode(original, retainContinuation: true);
        using GhosttyTerminal restored = GhosttySnapshot.Decode(encoded.ToArray(), retainContinuation: true);
        Assert.Equal(GhosttySnapshot.Encode(direct), GhosttySnapshot.Encode(restored));
        direct.Write("mNEXT\u001b[?47h!\u001b[?47l?"u8);
        restored.Write("mNEXT\u001b[?47h!\u001b[?47l?"u8);
        Assert.Equal(GhosttySnapshot.Encode(direct), GhosttySnapshot.Encode(restored));

        void WritePayload(GhosttySnapshotRecordTag tag)
        {
            GhosttySnapshotFraming.WriteRecord(encoded, tag, payload.GetBuffer().AsSpan(0, (int)payload.Length));
            payload.SetLength(0);
        }
    }

    [Fact]
    public void InvalidStagingCannotMutateAnExistingPublishedScreen()
    {
        TerminalScreen destination = new(4, 2);
        destination.GetRow(0)[0].Codepoint = 'X';
        TerminalScreen staging = TerminalScreen.CreateSnapshotStorage(4, 2, 0, TerminalTheme.Dark);
        Assert.Throws<InvalidDataException>(() => staging.InstallSnapshotRows([new(4)], null, 0));
        Assert.Throws<InvalidDataException>(() => staging.InstallSnapshotRows([new(4), new(4)], null, 1));
        Assert.Equal(0, staging.TotalRows);
        Assert.Equal('X', destination.GetRow(0)[0].Codepoint);
        staging.InstallSnapshotRows([new(4), new(4)], null, 0);
        Assert.Null(staging.GetSnapshotRows(1));
        Assert.Throws<InvalidOperationException>(() => staging.InstallSnapshotRows([new(4), new(4)], null, 0));
    }

    [Fact]
    public void PublicationTransfersRegistriesWithoutAllocationAndRetainsLockAndCopyIsolation()
    {
        TerminalScreen target = new(4, 2);
        object gate = target.SyncRoot;
        TerminalScreen frozen = target.CreateStateCopy();
        target.AdoptStateFrom(target.CreateStateCopy()); // Warm the publication path.
        TerminalScreen source = new(4, 2);
        for (uint i = 0; i < 1000; i++) source.RegisterHyperlink("uri"u8, [], i);
        int token = source.RegisterHyperlink("final"u8, "id"u8, 0);
        source.GetRow(0)[0].HyperlinkId = token;
        int legacy = source.RegisterHyperlink("legacy");
        long before;
        long allocated;
        lock (gate)
        {
            before = GC.GetAllocatedBytesForCurrentThread();
            target.AdoptStateFrom(source);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.Equal(0, allocated);
        Assert.Same(gate, target.SyncRoot);
        Assert.True(target.TryGetHyperlink(token, out TerminalHyperlink? link));
        Assert.Equal("final"u8.ToArray(), link!.UriBytes.ToArray());
        Assert.Equal(legacy, target.RegisterHyperlink("legacy"));
        Assert.False(frozen.TryGetHyperlink(token, out _));
        TerminalScreen nextCopy = target.CreateStateCopy();
        target.ClearAll();
        Assert.True(nextCopy.TryGetHyperlink(token, out _));
        Assert.Equal(token, nextCopy.GetRow(0)[0].HyperlinkId);
    }
}
