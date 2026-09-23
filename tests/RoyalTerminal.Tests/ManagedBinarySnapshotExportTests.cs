// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedBinarySnapshotExportTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("")]
    [InlineData("\u001b[31;44;1;4:3;58:5:7mhello界é")]
    [InlineData("\u001b[?69h\u001b[3;14s\u001b[2;4r\u001b[?6h\u001b[2;3H")]
    [InlineData("\u001b[3g\u001b[1;4H\u001bH\u001b)0\u000eqq\u001b7\u001b(B")]
    [InlineData("\u001b[31m\u001b7\u001b[?47h\u001b[32;4:4malt\u001b7\u001b[?47l")]
    [InlineData("\u001b[31m\u001b7\u001b[?1049h\u001b[32malt\u001b7")]
    [InlineData("\u001b[>31u\u001b[>7u\u001b[>4;2m\u001b[>1s\u001b]22;crosshair\a")]
    [InlineData("\u001b]4;2;#123456\a\u001b]10;#123456\a\u001b]2;title\a\u001b]7;file:///tmp\a")]
    [InlineData("\u001b]8;id=current;https://example.test\a\u001b[1\"q\u001b]133;P;k=c\a")]
    [InlineData("\u001b]2;unfinished")]
    [InlineData("\u001b[38:2::1:2")]
    public void NativeAcceptsEveryEncodedStateAndCanonicalRoundTripIsLossless(string input)
    {
        TerminalScreen screen = new(16, 4, 10000);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes(input));
        processor.PasswordInput = true;
        byte[] encoded = processor.GetBinarySnapshot();
        using ManagedTerminalSnapshot own = ManagedTerminalSnapshot.Restore(encoded);
        Assert.Equal(encoded, own.Processor.GetBinarySnapshot());
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native binary export differential available: {available}");
        if (!available) return;
        using GhosttyTerminal native = GhosttySnapshot.Decode(encoded, retainContinuation: true);
        using ManagedTerminalSnapshot bounced = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(native));
        Assert.Equal(encoded, bounced.Processor.GetBinarySnapshot());
        Assert.Equal(processor.GetContinuation(), native.GetContinuation());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void StreamingHistoryIsNewestPageFirstAndDoesNotDuplicateReadyRows(int widthChange)
    {
        TerminalScreen screen = new(80, 4, 10000);
        using BasicVtProcessor processor = new(screen);
        for (int i = 0; i < 1100; i++) processor.Process(Encoding.ASCII.GetBytes($"row {i:D4}\r\n"));
        if (widthChange != 0) processor.ResizeScreen(widthChange == 1 ? 60 : 100, 4, 0, 0, reflowOnResize: false);
        byte[] bytes = processor.GetBinarySnapshot();
        using ManagedTerminalSnapshotDecoder reader = new(bytes);
        using ManagedTerminalSnapshot restored = reader.Ready();
        Assert.Equal(4, restored.Screen.TotalRows);
        int rows = 0, pages = 0;
        while (reader.Next() is { } progress) { rows += progress.RowsApplied; pages++; }
        Assert.True(pages >= 2);
        Assert.Equal(screen.TotalRows - 4, rows);
        Assert.Equal(bytes, restored.Processor.GetBinarySnapshot());
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyTerminal native = GhosttySnapshot.Decode(bytes, retainContinuation: true);
        using ManagedTerminalSnapshot bounced = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(native));
        Assert.Equal(bytes, bounced.Processor.GetBinarySnapshot());
    }

    [Fact]
    public void DenseLinksSplitAtNativeCapacityBoundariesWithoutLosingIdentities()
    {
        TerminalScreen screen = new(80, 20, 10000);
        using BasicVtProcessor processor = new(screen);
        for (int row = 0; row < 20; row++)
        for (int col = 0; col < 80; col++)
        {
            ref TerminalCell cell = ref screen.GetRow(row)[col];
            cell.Codepoint = 'X';
            cell.HyperlinkId = screen.RegisterHyperlink(Encoding.ASCII.GetBytes($"https://example/{row}/{col}"), [], (uint)(row * 80 + col));
        }
        byte[] bytes = processor.GetBinarySnapshot();
        using GhosttySnapshotStateReader reader = new(bytes, new());
        Assert.Equal(2, reader.ReadReady().Screens[0].Pages.Length);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(bytes);
        Assert.Equal(bytes, restored.Processor.GetBinarySnapshot());
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyTerminal native = GhosttySnapshot.Decode(bytes, retainContinuation: true);
        using ManagedTerminalSnapshot bounced = ManagedTerminalSnapshot.Restore(GhosttySnapshot.Encode(native));
        Assert.Equal(bytes, bounced.Processor.GetBinarySnapshot());
    }

    [Fact]
    public void StreamIsLeftOpenMatchesArrayAndCaptureDoesNotPublishOrEndRenderHold()
    {
        TerminalScreen screen = new(8, 3);
        using BasicVtProcessor processor = new(screen);
        processor.Process("before\r\u001b[?2026hAFTER"u8);
        Assert.Equal('b', screen.GetViewportRow(0)[0].Codepoint);
        using MemoryStream outputStream = new();
        processor.WriteBinarySnapshotTo(outputStream);
        Assert.True(outputStream.CanWrite);
        Assert.Equal(processor.GetBinarySnapshot(), outputStream.ToArray());
        Assert.Equal('b', screen.GetViewportRow(0)[0].Codepoint);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(outputStream.ToArray());
        Assert.Equal('A', restored.Screen.GetViewportRow(0)[0].Codepoint);
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal('A', screen.GetViewportRow(0)[0].Codepoint);
    }

    [Fact]
    public void UnretainedContinuationAndPreflightBoundsEmitNoEnvelope()
    {
        using BasicVtProcessor disabled = new(new TerminalScreen(8, 3), new() { ContinuationMaxBytes = 0 });
        using BasicVtProcessor processor = new(new TerminalScreen(8, 3));
        using MemoryStream outputStream = new();
        Assert.Throws<InvalidOperationException>(() => disabled.WriteBinarySnapshotTo(outputStream));
        Assert.Equal(0, outputStream.Length);
        Assert.Throws<InvalidDataException>(() => processor.WriteBinarySnapshotTo(outputStream, new(MaximumCells: 1)));
        Assert.Equal(0, outputStream.Length);
        Assert.Throws<InvalidDataException>(() => processor.WriteBinarySnapshotTo(outputStream, new(MaximumPages: 0)));
        Assert.Equal(0, outputStream.Length);
        processor.Process("\u001b]2;unfinished"u8);
        Assert.Throws<InvalidDataException>(() => processor.WriteBinarySnapshotTo(outputStream, new(MaximumContinuationBytes: 1)));
        Assert.Equal(0, outputStream.Length);
        Assert.Throws<ArgumentNullException>(() => processor.WriteBinarySnapshotTo(null!));
        using MemoryStream readOnly = new([], writable: false);
        Assert.Throws<ArgumentException>(() => processor.WriteBinarySnapshotTo(readOnly));
    }

    [Fact]
    public void WriterFailureLeavesProcessorUsableAndNeverWritesFinish()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 3));
        processor.Process("text"u8);
        byte[] expected = processor.GetBinarySnapshot();
        using FailAfterEnvelope stream = new();
        Assert.Throws<IOException>(() => processor.WriteBinarySnapshotTo(stream));
        Assert.Equal(GhosttySnapshotFraming.Envelope.ToArray(), stream.ToArray());
        Assert.Equal(expected, processor.GetBinarySnapshot());
        processor.Process("more"u8);
    }

    private sealed class FailAfterEnvelope : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length != 0) throw new IOException("injected failure");
            base.Write(buffer);
        }
    }
}
