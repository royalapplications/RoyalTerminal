// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedSnapshotProcessorStateTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> RuntimeStates()
    {
        string[] states = ["", "\u001b[3g\u001b[1;4H\u001bH\u001b[1;10H\u001bH",
            "\u001b[2;4r\u001b[?69h\u001b[3;14s\u001b[?6h\u001b[2;3H",
            "\u001b[1;3;4:3;5;7;8;9;53;38:5:2;48:2::12:34:56;58:5:3m",
            "\u001b[1\"q\u001b]133;P;k=c\a", "\u001b]133;A;cl=w;redraw=last\a\u001b]133;I\a",
            "\u001b]8;id=current;https://snapshot.example\a", "\u001b*0\u001bN",
            "\u001b[1;16H#", "\u001b(A\u001b~", "\u001bV\u001b[44m"];
        foreach (bool alternate in new[] { false, true })
        foreach (string state in states) yield return [alternate, state];
    }

    [Theory]
    [MemberData(nameof(RuntimeStates))]
    public void InstalledCurrentAndSavedStateContinuesLikeNative(bool alternate, string state)
    {
        if (!Available()) return;
        using GhosttyTerminal source = new(16, 4);
        source.Write("primary\u001b[31m\u001b(A\u001b7\u001b[?47h\u001b[32m\u001b)0\u000ealternate\u001b7\u001b[?47l\u001b[0m\u001b(B\u000f"u8);
        if (alternate) source.Write("\u001b[?47h"u8);
        source.Write(Encoding.UTF8.GetBytes(state));
        using GhosttyTerminal native = GhosttySnapshot.Decode(GhosttySnapshot.Encode(source));
        GhosttySnapshotReadyState ready = Read(native);
        using BasicVtProcessor managed = Stage(ready, out TerminalScreen screen);
        Compare(native, managed, screen);
        List<byte> expected = [], actual = [];
        GhosttyVtNative.GhosttyTerminalWritePtyCallback callback = (_, _, data, length) =>
        {
            byte[] bytes = new byte[checked((int)length)]; Marshal.Copy(data, bytes, 0, bytes.Length); expected.AddRange(bytes);
        };
        native.SetWritePtyCallback(Marshal.GetFunctionPointerForDelegate(callback));
        managed.ResponseCallback = bytes => actual.AddRange(bytes);
        try
        {
            foreach (string command in new[] { "\u001bP$qm\u001b\\\u001b[6n", "q#", "\tQ", "\u001b[2b",
                "\u001b8#q", "\u001b[?1049hALT", "\u001b[?1049lR", "\u001b[?47h\u001b8q#", "\u001b[?47l!" })
            {
                byte[] bytes = Encoding.UTF8.GetBytes(command);
                native.Write(bytes); managed.Process(bytes);
                Compare(native, managed, screen); Assert.Equal(expected, actual);
            }
        }
        finally { native.SetWritePtyCallback(0); GC.KeepAlive(callback); }
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(65U)]
    [InlineData(0x10FFFFU)]
    [InlineData(uint.MaxValue)]
    public void GeometryTabsAndNullablePreviousCharacterInstallExactly(uint previous)
    {
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        byte[] terminal = records[0].Payload;
        BinaryPrimitives.WriteUInt32LittleEndian(terminal.AsSpan(4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(terminal.AsSpan(8), 0x80000000);
        BinaryPrimitives.WriteUInt32LittleEndian(terminal.AsSpan(25), previous);
        terminal[GhosttySnapshotTerminalHeader.Length] = 2; // Exactly column one, not the constructor's column zero.
        GhosttySnapshotReadyState ready = Read(SnapshotTestRecords.Encode(records));
        using BasicVtProcessor managed = Stage(ready, out TerminalScreen screen);
        AssertGeometry(ready, managed);
        Assert.False(managed.IsSnapshotTabStop(0)); Assert.True(managed.IsSnapshotTabStop(1));
        byte[]? response = null; managed.ResponseCallback = bytes => response = bytes;
        managed.Process("\u001b[16t"u8);
        Assert.Equal("\u001b[6;715827882;2147483647t", Encoding.ASCII.GetString(response!));
        managed.ResizeScreen(3, 4, -1, -2, reflowOnResize: false);
        Assert.Equal((0U, 0U), (managed.SnapshotGeometry.Width, managed.SnapshotGeometry.Height));
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void CurrentAndSavedCursorCoordinatesClampAgainstTheirDifferentExtents(bool alternate, int physicalWidth)
    {
        if (!Available()) return;
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        records[0].Payload[21] = alternate ? (byte)1 : (byte)0; records[0].Payload[22] = 0;
        for (int i = 0; i < records.Count; i++)
        {
            SnapshotTestRecord record = records[i];
            if (record.Tag == GhosttySnapshotRecordTag.Continuation) records[i] = new(record.Tag, []);
            if (record.Tag == GhosttySnapshotRecordTag.Page && physicalWidth != 2)
            {
                TerminalRow[] rows = [new(physicalWidth), new(physicalWidth), new(physicalWidth)];
                using MemoryStream payload = new();
                GhosttySnapshotLivePage.Capture(rows, new TerminalScreen(2, 3), 100).WritePayloadTo(payload);
                records[i] = new(record.Tag, payload.ToArray());
            }
            if (record.Tag != GhosttySnapshotRecordTag.Screen) continue;
            BinaryPrimitives.WriteUInt16LittleEndian(record.Payload.AsSpan(12), ushort.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(record.Payload.AsSpan(14), ushort.MaxValue);
            record.Payload[17] |= 1;
            if (record.Payload[52] == 1)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(record.Payload.AsSpan(53), ushort.MaxValue);
                BinaryPrimitives.WriteUInt16LittleEndian(record.Payload.AsSpan(55), ushort.MaxValue);
                record.Payload[73] |= 2;
            }
        }
        using GhosttyTerminal native = GhosttySnapshot.Decode(SnapshotTestRecords.Encode(records));
        using BasicVtProcessor managed = Stage(Read(native), out TerminalScreen screen);
        Compare(native, managed, screen);
        Assert.Equal(physicalWidth - 1, managed.CursorCol);
        foreach (string command in new[] { "\u001b8X", "\u001b[?47h\u001b8Y", "\u001b[?47l\u001b8Z" })
        {
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            native.Write(bytes); managed.Process(bytes); Compare(native, managed, screen);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    public void PromptClickAndDerivedSeenStateMatchNative(int kind, int value)
    {
        if (!Available()) return;
        List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
        foreach (SnapshotTestRecord record in records)
            if (record.Tag == GhosttySnapshotRecordTag.Screen)
            { record.Payload[50] = (byte)kind; record.Payload[51] = (byte)value; }
        using GhosttyTerminal native = GhosttySnapshot.Decode(SnapshotTestRecords.Encode(records));
        using BasicVtProcessor managed = Stage(Read(native), out _);
        Assert.Equal(native.GetPromptState(), managed.PromptState);
    }

    [Fact]
    public void CharsetInstallationCoversAllWireValuesWithoutAllocation()
    {
        for (int bits = 0; bits <= ushort.MaxValue; bits++)
        {
            GhosttySnapshotCharset value = GhosttySnapshotCharset.Read((ushort)bits);
            Assert.Equal(value.Bits, ManagedCharsetState.FromSnapshot(value).Bits);
        }
        GhosttySnapshotCharset state = GhosttySnapshotCharset.Read(0x4321);
        for (int i = 0; i < 1000; i++) _ = ManagedCharsetState.FromSnapshot(state);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = ManagedCharsetState.FromSnapshot(state);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void SgrStatusReportsPreserveUnderlineVariantsAndNativeAttributeOrder(int underline)
    {
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        byte[]? expected = null, actual = null;
        native.ResponseCallback = bytes => expected = bytes; managed.ResponseCallback = bytes => actual = bytes;
        // Ghostty's DECRQSS omits underline color even though VT style export
        // includes it. Preserve that distinction rather than inventing a reply.
        byte[] sequence = Encoding.ASCII.GetBytes($"\u001b[1;2;3;4:{underline};5;7;8;9;53;38:5:20;48:2::1:2:3;58:5:4m\u001bP$qm\u001b\\");
        native.Process(sequence); managed.Process(sequence);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InstallationDoesNotPublishModesRepliesOrAlterCellsAndRejectsWrongGeometry()
    {
        GhosttySnapshotReadyState ready = Read(GhosttySnapshotFramingTests.Fixture("complete-v1.hex"));
        TerminalScreen screen = GhosttySnapshotLiveScreen.Stage(ready, TerminalTheme.Dark, 10000);
        using BasicVtProcessor managed = new(screen);
        managed.ResponseCallback = _ => Assert.Fail("Snapshot installation emitted a reply.");
        managed.ModeChanged += (_, _) => Assert.Fail("Snapshot installation emitted a mode event.");
        TerminalRow row = screen.GetViewportRow(0); TerminalCell cell = row[0];
        managed.InstallSnapshotGeometry(ready.Terminal);
        foreach (GhosttySnapshotScreen state in ready.Screens) managed.InstallSnapshotScreenState(state.State);
        Assert.Same(row, screen.GetViewportRow(0)); Assert.Equal(cell, row[0]);
        using BasicVtProcessor wrong = new(new TerminalScreen(80, 24));
        Assert.Throws<InvalidOperationException>(() => wrong.InstallSnapshotGeometry(ready.Terminal));
        Assert.Throws<ArgumentOutOfRangeException>(() => managed.GetSnapshotCharset(2));
    }

    private static BasicVtProcessor Stage(GhosttySnapshotReadyState ready, out TerminalScreen screen)
    {
        screen = GhosttySnapshotLiveScreen.Stage(ready, TerminalTheme.Dark, 10000);
        BasicVtProcessor managed = new(screen);
        managed.InstallSnapshotColors(ready.Terminal, TerminalTheme.Dark);
        managed.InstallSnapshotModes(ready.Terminal.Header);
        managed.InstallSnapshotCursorPolicy(ready.Terminal.Header);
        managed.InstallSnapshotGeometry(ready.Terminal);
        foreach (GhosttySnapshotScreen state in ready.Screens) managed.InstallSnapshotScreenState(state.State);
        return managed;
    }

    private static void AssertGeometry(GhosttySnapshotReadyState ready, BasicVtProcessor managed)
    {
        GhosttySnapshotTerminalHeader h = ready.Terminal.Header;
        Assert.Equal((h.PixelWidth, h.PixelHeight, h.ScrollTop, h.ScrollBottom, h.ScrollLeft, h.ScrollRight,
            h.PreviousCodepoint is { } cp ? (int)cp : -1), managed.SnapshotGeometry);
        for (int col = 0; col < h.Columns; col++) Assert.Equal(ready.Terminal.IsTabStop(col), managed.IsSnapshotTabStop(col));
    }

    private static void Compare(GhosttyTerminal native, BasicVtProcessor managed, TerminalScreen screen)
    {
        GhosttySnapshotReadyState ready = Read(native);
        AssertGeometry(ready, managed);
        Assert.Equal((native.GetCursorX(), native.GetCursorY()), ((ushort)managed.CursorCol, (ushort)managed.CursorRow));
        Assert.Equal(native.GetPromptState(), managed.PromptState);
        Assert.Equal(ready.Terminal.Header.CurrentModes, managed.SnapshotCurrentModes);
        TerminalScreen expected = GhosttySnapshotLiveScreen.Stage(ready, TerminalTheme.Dark, 10000);
        foreach (GhosttySnapshotScreen state in ready.Screens)
        {
            Assert.Equal(state.State.Charset.Bits, managed.GetSnapshotCharset(state.State.Key));
            TerminalRowBuffer erows = expected.GetSnapshotRows(state.State.Key)!, arows = screen.GetSnapshotRows(state.State.Key)!;
            Assert.Equal(erows.Count, arows.Count);
            for (int r = 0; r < erows.Count; r++)
            {
                TerminalRow e = erows[r], a = arows[r];
                Assert.Equal((e.Columns, e.WrapsToNext, e.IsWrapContinuation, e.SemanticPrompt),
                    (a.Columns, a.WrapsToNext, a.IsWrapContinuation, a.SemanticPrompt));
                for (int col = 0; col < e.Columns; col++)
                {
                    TerminalCell ec = e[col], ac = a[col];
                    Assert.Equal((ec.Codepoint, ec.Width, ec.Grapheme, ec.IsProtected, ec.SemanticContent),
                        (ac.Codepoint, ac.Width, ac.Grapheme, ac.IsProtected, ac.SemanticContent));
                    Assert.Equal((ec.ForegroundIdentity, ec.BackgroundIdentity, ec.UnderlineIdentity, ec.Attributes, ec.Decorations, ec.UnderlineStyle),
                        (ac.ForegroundIdentity, ac.BackgroundIdentity, ac.UnderlineIdentity, ac.Attributes, ac.Decorations, ac.UnderlineStyle));
                    expected.TryGetHyperlinkUrl(ec.HyperlinkId, out string? eurl); screen.TryGetHyperlinkUrl(ac.HyperlinkId, out string? aurl);
                    Assert.Equal(eurl, aurl);
                }
            }
        }
    }

    private static GhosttySnapshotReadyState Read(GhosttyTerminal terminal) => Read(GhosttySnapshot.Encode(terminal));
    private static GhosttySnapshotReadyState Read(byte[] bytes)
    {
        using GhosttySnapshotStateReader reader = new(bytes, new()); return reader.ReadReady();
    }
    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable(); output.WriteLine($"Native processor-state differential available: {available}"); return available;
    }
}
