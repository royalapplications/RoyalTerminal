// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotTerminalTests(ITestOutputHelper output)
{
    [Fact]
    public void HeaderMatchesUpstreamGoldenAndOwnsItsData()
    {
        byte[] fixture = GhosttySnapshotFramingTests.Fixture("terminal-header-v1.hex");
        byte[] input = fixture.ToArray();
        GhosttySnapshotTerminalHeader header = GhosttySnapshotTerminalHeader.Read(input, 1_000_000);
        Array.Clear(input);
        Assert.Equal((0x102, 0x304), (header.Columns, header.Rows));
        Assert.Equal((0x05060708u, 0x090A0B0Cu), (header.PixelWidth, header.PixelHeight));
        Assert.Equal((1, 2, 3, 4), (header.ScrollTop, header.ScrollBottom, header.ScrollLeft, header.ScrollRight));
        Assert.Equal((1, 1, 2), ((int)header.StatusDisplay, header.ActiveScreenKey, header.ScreenCount));
        Assert.Equal(65u, header.PreviousCodepoint);
        Assert.True(header.CursorIsDefault);
        Assert.Equal(3, header.CursorDefaultStyle);
        Assert.True(header.CursorDefaultBlink);
        Assert.Equal(2, header.ShellRedraw);
        Assert.True(header.ModifyOtherKeys2);
        Assert.Equal((4, 4, 33), ((int)header.MouseEvent, (int)header.MouseFormat, (int)header.MouseShape));
        Assert.False(header.MouseShiftCapture);
        Assert.True(header.PasswordInput);
        Assert.Equal((1UL, 1UL << 41, 1UL | (1UL << 41)), (header.CurrentModes, header.SavedModes, header.DefaultModes));
        Assert.Equal(new(0x010203, null), header.Background);
        Assert.Equal(new(null, 0x040506), header.Foreground);
        Assert.Equal(new(0x070809, 0x0A0B0C), header.CursorColor);
        Assert.Null(header.MaximumScrollbackBytes);
        Assert.Equal(0x0102030405060708UL, header.MaximumScrollbackRows);
        using MemoryStream encoded = new();
        header.WriteTo(encoded);
        Assert.Equal(fixture, encoded.ToArray());
    }

    [Fact]
    public void HeaderNormalizesSemanticsButRejectsStructuralErrors()
    {
        byte[] fixture = GhosttySnapshotFramingTests.Fixture("terminal-header-v1.hex");
        for (int length = 0; length < fixture.Length; length++)
            Assert.Throws<EndOfStreamException>(() => GhosttySnapshotTerminalHeader.Read(fixture.AsSpan(0, length), 1_000_000));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotTerminalHeader.Read(fixture, 258 * 772 - 1));
        foreach (int offset in new[] { 0, 2, 23 })
        {
            byte[] bad = fixture.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(offset), 0);
            Assert.Throws<InvalidDataException>(() => GhosttySnapshotTerminalHeader.Read(bad, int.MaxValue));
        }
        byte[] invalid = fixture.ToArray();
        invalid.AsSpan(12, 11).Fill(255);
        invalid.AsSpan(25, 62).Fill(255);
        GhosttySnapshotTerminalHeader header = GhosttySnapshotTerminalHeader.Read(invalid, 1_000_000);
        Assert.Equal((0, 771, 0, 257), (header.ScrollTop, header.ScrollBottom, header.ScrollLeft, header.ScrollRight));
        Assert.Equal((0, 0), ((int)header.StatusDisplay, header.ActiveScreenKey));
        Assert.Null(header.PreviousCodepoint);
        Assert.True(header.CursorIsDefault);
        Assert.Equal(1, header.CursorDefaultStyle);
        Assert.Null(header.CursorDefaultBlink);
        Assert.Equal(0, header.ShellRedraw);
        Assert.False(header.ModifyOtherKeys2);
        Assert.Equal((0, 0, 8), ((int)header.MouseEvent, (int)header.MouseFormat, (int)header.MouseShape));
        Assert.Null(header.MouseShiftCapture);
        Assert.False(header.PasswordInput);
        Assert.Equal(GhosttySnapshotTerminalHeader.ModeMask, header.CurrentModes);
        Assert.Equal(header.CurrentModes, header.SavedModes);
        Assert.Equal(header.CurrentModes, header.DefaultModes);
        Assert.Equal(default, header.Background);
        Assert.Equal(default, header.Foreground);
        Assert.Equal(default, header.CursorColor);
    }

    [Fact]
    public void HeaderPreservesSingleCellMarginsAndDistinguishesZeroFromAbsentValues()
    {
        byte[] bytes = GhosttySnapshotFramingTests.Fixture("terminal-header-v1.hex");
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(23), 1);
        bytes.AsSpan(25, 4).Clear();
        bytes[31] = 1;
        bytes[36] = 2;
        bytes.AsSpan(63, 24).Clear();
        bytes[63] = 1;
        bytes.AsSpan(87, 8).Clear();
        bytes.AsSpan(95, 8).Fill(255);
        GhosttySnapshotTerminalHeader header = GhosttySnapshotTerminalHeader.Read(bytes, 1_000_000);
        Assert.Equal((1, 1, 0, 257), (header.ScrollTop, header.ScrollBottom, header.ScrollLeft, header.ScrollRight));
        Assert.Equal(0, header.ActiveScreenKey);
        Assert.Equal(0u, header.PreviousCodepoint);
        Assert.False(header.CursorDefaultBlink);
        Assert.True(header.MouseShiftCapture);
        Assert.Equal(new(0, null), header.Background);
        Assert.Equal(0UL, header.MaximumScrollbackBytes);
        Assert.Null(header.MaximumScrollbackRows);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(25), 0xD800);
        Assert.Null(GhosttySnapshotTerminalHeader.Read(bytes, 1_000_000).PreviousCodepoint);
    }

    [Fact]
    public void PayloadRetainsSparsePaletteBinaryStringsAndCanonicalTabsWithoutPerWriteAllocations()
    {
        byte[] input = Payload();
        GhosttySnapshotTerminalState state = GhosttySnapshotTerminalState.Read(input, 18, 6);
        input.AsSpan().Clear();
        Assert.True(state.IsTabStop(0));
        Assert.True(state.IsTabStop(8));
        Assert.False(state.IsTabStop(1));
        Assert.Equal(new byte[] { 0, 255, 128 }, state.Pwd.ToArray());
        Assert.Equal(new byte[] { 254, 0, 65 }, state.Title.ToArray());
        for (int index = 0; index < 256; index++)
        {
            bool overridden = index is 0 or 7 or 8 or 255;
            Assert.Equal((uint)(index << 16 | 0x1234), state.OriginalPaletteColor(index));
            Assert.Equal(overridden, state.HasPaletteOverride(index));
            Assert.Equal(overridden ? (uint)(0xAA5500 | index) : state.OriginalPaletteColor(index), state.CurrentPaletteColor(index));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => state.IsTabStop(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.IsTabStop(9));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.CurrentPaletteColor(256));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.OriginalPaletteColor(-1));
        byte[] canonical = Encode(state);
        Assert.Equal(1, canonical[104]); // Ignore seven unused high bits in the last tab byte.
        Assert.Equal(canonical, Encode(GhosttySnapshotTerminalState.Read(canonical, 18, 6)));
        for (int i = 0; i < 100; i++) state.WritePayloadTo(Stream.Null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) state.WritePayloadTo(Stream.Null);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void PayloadRejectsTruncationTrailingDataAndLimitsBeforeCopyingStrings()
    {
        byte[] bytes = Payload();
        for (int length = 0; length < bytes.Length; length++)
            Assert.Throws<EndOfStreamException>(() => GhosttySnapshotTerminalState.Read(bytes.AsSpan(0, length), 18, 6));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotTerminalState.Read(bytes, 17, 6));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotTerminalState.Read(bytes, 18, 5));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotTerminalState.Read([.. bytes, 0], 18, 6));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(103 + 2 + 800 + 12), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotTerminalState.Read(bytes, 18, int.MaxValue));
    }

    [Fact]
    public void FullGoldenTerminalReencodesByteForByte()
    {
        byte[] fixture = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        Assert.Equal(fixture, RewriteTerminal(fixture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeTerminalStateRoundTripsThroughManagedCodec(bool alternate)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native snapshot differential available: {available}");
        if (!available) return;
        using GhosttyTerminal terminal = new(13, 4);
        terminal.SetScrollbackMaxBytes(1234567);
        terminal.SetScrollbackMaxLines(987);
        terminal.Write("\u001b]2;Snapshot title\u0007\u001b]7;file:///tmp/path\u0007\u001b]4;7;rgb:12/34/56\u0007\u001b]10;#fedcba\u0007"u8);
        terminal.Write("\u001b[3g\u001b[5G\u001bH\u001b[?69h\u001b[3;11s\u001b[2;4r\u001b[?6h\u001b[?1003h\u001b[?1006h\u001b[?2004h\u001b[?2004s\u001b[?2004l"u8);
        if (alternate) terminal.Write("\u001b[?1049h"u8);
        terminal.Write("TEST"u8);
        byte[] source = GhosttySnapshot.Encode(terminal);
        byte[] rewritten = RewriteTerminal(source);
        Assert.Equal(source, rewritten);
        using GhosttyTerminal restored = GhosttySnapshot.Decode(rewritten);
        // Compare every terminal-wide field again after native installation, not just displayed text.
        Assert.Equal(ExtractTerminal(source), ExtractTerminal(GhosttySnapshot.Encode(restored)));
    }

    [Fact]
    public void MalformedSemanticHeadersNormalizeExactlyLikeNative()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native snapshot differential available: {available}");
        if (!available) return;
        using GhosttyTerminal terminal = new(13, 4);
        byte[] source = GhosttySnapshot.Encode(terminal);
        byte[] payload = ExtractTerminal(source);
        Random random = new(72319);
        for (int iteration = 0; iteration < 100; iteration++)
        {
            // Leave dimensions, screen count, allocation policies and record boundaries intact.
            random.NextBytes(payload.AsSpan(12, 11));
            random.NextBytes(payload.AsSpan(25, 62));
            byte[] malformed = ReframeTerminal(source, payload);
            using GhosttyTerminal direct = GhosttySnapshot.Decode(malformed);
            using GhosttyTerminal managed = GhosttySnapshot.Decode(RewriteTerminal(malformed));
            Assert.Equal(ExtractTerminal(GhosttySnapshot.Encode(direct)),
                ExtractTerminal(GhosttySnapshot.Encode(managed)));
        }
    }

    private static byte[] Payload()
    {
        byte[] header = GhosttySnapshotFramingTests.Fixture("terminal-header-v1.hex");
        BinaryPrimitives.WriteUInt16LittleEndian(header, 9);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 2);
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(header);
        writer.Write(new byte[] { 1, 255 });
        for (int i = 0; i < 256; i++) writer.Write(new byte[] { (byte)i, 0x12, 0x34 });
        byte[] mask = new byte[32]; mask[0] = 0x81; mask[1] = 1; mask[31] = 128;
        writer.Write(mask);
        foreach (int index in new[] { 0, 7, 8, 255 }) writer.Write(new byte[] { 0xAA, 0x55, (byte)index });
        writer.Write(3u); writer.Write(new byte[] { 0, 255, 128 });
        writer.Write(3u); writer.Write(new byte[] { 254, 0, 65 });
        return stream.ToArray();
    }

    private static byte[] Encode(GhosttySnapshotTerminalState state)
    {
        using MemoryStream stream = new();
        state.WritePayloadTo(stream);
        return stream.ToArray();
    }

    private static byte[] ExtractTerminal(byte[] snapshot)
    {
        using GhosttySnapshotRecordReader reader = new(snapshot, 1_000_000);
        reader.ReadEnvelope();
        Assert.Equal(GhosttySnapshotRecordTag.Terminal, reader.ReadRecord(out ReadOnlySpan<byte> payload));
        return payload.ToArray();
    }

    private static byte[] RewriteTerminal(byte[] snapshot)
        => ReframeTerminal(snapshot, Encode(GhosttySnapshotTerminalState.Read(ExtractTerminal(snapshot), 1_000_000, 4096)));

    private static byte[] ReframeTerminal(byte[] snapshot, byte[] terminalPayload)
    {
        using GhosttySnapshotRecordReader reader = new(snapshot, 1_000_000);
        reader.ReadEnvelope();
        using MemoryStream result = new();
        result.Write(GhosttySnapshotFraming.Envelope);
        Assert.Equal(GhosttySnapshotRecordTag.Terminal, reader.ReadRecord(out _));
        GhosttySnapshotFraming.WriteRecord(result, GhosttySnapshotRecordTag.Terminal, terminalPayload);
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            GhosttySnapshotFraming.WriteRecord(result, tag, payload);
            if (tag == GhosttySnapshotRecordTag.Finish) break;
        }
        return result.ToArray();
    }
}
