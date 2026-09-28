// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotScreenTests(ITestOutputHelper output)
{
    [Fact]
    public void ScreenAndSavedCursorMatchUpstreamGoldenBytesAndOwnTheirStorage()
    {
        byte[] expected = Fixture();
        byte[] input = expected.ToArray();
        GhosttySnapshotScreenState state = GhosttySnapshotScreenState.Read(input, 1000, 1000);
        Array.Clear(input);
        Assert.Equal((1, 0x203), (state.Key, state.PageCount));
        Assert.Equal(0x0102030405060708UL, state.HistoryRows);
        Assert.Equal((0x405, 0x607, 3), (state.CursorX, state.CursorY, (int)state.CursorStyle));
        Assert.True(state.PendingWrap);
        Assert.False(state.Protected);
        Assert.Equal(2, state.SemanticContent);
        Assert.True(state.SemanticContentClearEol);
        Assert.Equal(new GhosttySnapshotColor(1, 127, 0, 0), state.Pen.Background);
        Assert.Equal(new GhosttySnapshotColor(2, 0x12, 0x34, 0x56), state.Pen.UnderlineColor);
        Assert.Equal(0x3FF, state.Pen.Flags);
        Assert.Equal(0x0A0B0C0Du, state.HyperlinkImplicitCounter);
        Assert.Equal((1, 3, 2), (state.Charset.GL, state.Charset.GR, state.Charset.SingleShift));
        for (int slot = 0; slot < 4; slot++) Assert.Equal(slot, state.Charset.GetCharset(slot));
        Assert.Equal(2, state.ProtectedMode);
        Assert.Equal(7, state.KittyKeyboardIndex);
        Assert.Equal(new byte[] { 1, 2, 4, 8, 16, 31, 0, 17 }, state.KittyKeyboardFlags.ToArray());
        Assert.Equal((2, 3), ((int)state.SemanticClickKind, (int)state.SemanticClickValue));
        GhosttySnapshotSavedCursor saved = state.SavedCursor!.Value;
        Assert.Equal((0x102, 0x304), ((int)saved.X, (int)saved.Y));
        Assert.True(saved.Protected && saved.PendingWrap && saved.Origin);
        Assert.Equal(default, saved.Pen);
        Assert.Equal(state.Charset, saved.Charset);
        Assert.True(state.TryGetHyperlink(out GhosttySnapshotHyperlink link));
        Assert.True(link.HasExplicitId);
        Assert.Equal(expected, Encode(state));
        for (int i = 0; i < 100; i++) state.WritePayloadTo(Stream.Null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) state.WritePayloadTo(Stream.Null);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ScreenDistinguishesStructuralTruncationFromDiscardableCursorHyperlinks()
    {
        byte[] fixture = Fixture();
        // Header and saved cursor are structural. Even the link kind byte must be present.
        for (int length = 0; length <= 76; length++)
            Assert.Throws<EndOfStreamException>(() => GhosttySnapshotScreenState.Read(fixture.AsSpan(0, length), 1000, 1000));
        // The remainder of the final hyperlink is recoverable at the SCREEN boundary.
        for (int length = 77; length < fixture.Length; length++)
            Assert.False(GhosttySnapshotScreenState.Read(fixture.AsSpan(0, length), 1000, 1000).TryGetHyperlink(out _));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotScreenState.Read([.. fixture, 0], 1000, 1000));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotScreenState.Read(fixture, 514, 1000));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotScreenState.Read(fixture, 1000, 0));
        foreach (int offset in new[] { 0, 2 })
        {
            byte[] invalid = fixture.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(offset), offset == 0 ? (ushort)2 : (ushort)0);
            Assert.Throws<InvalidDataException>(() => GhosttySnapshotScreenState.Read(invalid, 1000, 1000));
        }
        byte[] none = [.. fixture.AsSpan(0, 76), 0];
        Assert.False(GhosttySnapshotScreenState.Read(none, 1000, 0).TryGetHyperlink(out _));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotScreenState.Read([.. none, 1], 1000, 0));
        byte[] unknown = [.. fixture.AsSpan(0, 76), 99, 0, 1, 2];
        Assert.Equal(none, Encode(GhosttySnapshotScreenState.Read(unknown, 1000, 0)));
        byte[] emptyUri = [.. fixture.AsSpan(0, 76), 1, 42, 0, 0, 0, 0, 0, 0, 0, 99];
        Assert.Equal(none, Encode(GhosttySnapshotScreenState.Read(emptyUri, 1000, 0)));
    }

    [Fact]
    public void ReservedStateNormalizesWithoutLosingSavedCursorOrIndependentLinkCounter()
    {
        byte[] bytes = Fixture();
        bytes.AsSpan(16, 18).Fill(255);
        bytes.AsSpan(38, 15).Fill(255);
        bytes.AsSpan(57, 19).Fill(255);
        GhosttySnapshotScreenState state = GhosttySnapshotScreenState.Read(bytes, 1000, 1000);
        Assert.Equal(1, state.CursorStyle);
        Assert.True(state.PendingWrap && state.Protected && state.SemanticContentClearEol);
        Assert.Equal(0, state.SemanticContent);
        Assert.Equal(default, state.Pen);
        Assert.Equal(0x0FFF, state.Charset.Bits);
        Assert.Equal(0, state.ProtectedMode);
        Assert.Equal(0, state.KittyKeyboardIndex);
        Assert.All(state.KittyKeyboardFlags.ToArray(), flags => Assert.Equal(31, flags));
        Assert.Equal((0, 0), ((int)state.SemanticClickKind, (int)state.SemanticClickValue));
        Assert.Equal(default, state.SavedCursor!.Value.Pen);
        Assert.Equal(7, state.SavedCursor.Value.Flags);
        Assert.Equal(0x0FFF, state.SavedCursor.Value.Charset.Bits);
        Assert.Equal(1, Encode(state)[52]);

        byte[] implicitLink = GhosttySnapshotFramingTests.Fixture("hyperlink-implicit-v1.hex");
        byte[] implicitScreen = [.. Fixture().AsSpan(0, 76), .. implicitLink];
        GhosttySnapshotScreenState implicitState = GhosttySnapshotScreenState.Read(implicitScreen, 1000, 1000);
        Assert.True(implicitState.TryGetHyperlink(out GhosttySnapshotHyperlink link));
        Assert.False(link.HasExplicitId);
        Assert.NotEqual(implicitState.HyperlinkImplicitCounter, link.ImplicitId);
        Assert.Equal(implicitScreen, Encode(implicitState));
    }

    [Fact]
    public void AbsentSavedCursorAndHyperlinkHaveNoSuffixAllocationsOrHiddenState()
    {
        byte[] bytes = [.. Fixture().AsSpan(0, 53), 0];
        bytes[52] = 0;
        GhosttySnapshotScreenState state = GhosttySnapshotScreenState.Read(bytes, 1000, 0);
        Assert.Null(state.SavedCursor);
        Assert.False(state.TryGetHyperlink(out _));
        Assert.Equal(bytes, Encode(state));
        for (int i = 0; i < 100; i++) state.WritePayloadTo(Stream.Null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) state.WritePayloadTo(Stream.Null);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(1, 0, 1, 0)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(1, 2, 0, 0)]
    [InlineData(2, 0, 2, 0)]
    [InlineData(2, 3, 2, 3)]
    [InlineData(2, 4, 0, 0)]
    [InlineData(3, 0, 0, 0)]
    public void SemanticClickRegistryMatchesUpstream(byte kind, byte value, byte expectedKind, byte expectedValue)
    {
        byte[] bytes = Fixture(); bytes[50] = kind; bytes[51] = value;
        GhosttySnapshotScreenState state = GhosttySnapshotScreenState.Read(bytes, 1000, 1000);
        Assert.Equal((expectedKind, expectedValue), (state.SemanticClickKind, state.SemanticClickValue));
    }

    [Fact]
    public void EveryCharsetWordRetainsDefinedValuesAndDropsOnlyInvalidShiftAndPadding()
    {
        for (int raw = 0; raw <= ushort.MaxValue; raw++)
        {
            GhosttySnapshotCharset charset = GhosttySnapshotCharset.Read((ushort)raw);
            int shift = (raw >> 12) & 7;
            Assert.Equal(raw & (shift > 4 ? 0x0FFF : 0x7FFF), charset.Bits);
            Assert.Equal((raw >> 8) & 3, charset.GL);
            Assert.Equal((raw >> 10) & 3, charset.GR);
            Assert.Equal(shift is 0 or > 4 ? (int?)null : shift - 1, charset.SingleShift);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => GhosttySnapshotCharset.Read(0).GetCharset(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => GhosttySnapshotCharset.Read(0).GetCharset(-1));
    }

    [Fact]
    public void CursorInstallationUsesPhysicalRowWidthAndSavedTerminalWidth()
    {
        GhosttySnapshotScreenState state = GhosttySnapshotScreenState.Read(Fixture(), 1000, 1000);
        Assert.Equal((79, 23, true), state.GetCursorPosition(80, 24));
        Assert.Equal((0x405, 23, false), state.GetCursorPosition(2000, 24));
        Assert.Equal((0, 0, true), state.GetCursorPosition(1, 1));
        GhosttySnapshotSavedCursor saved = state.SavedCursor!.Value.Clamp(80, 24);
        Assert.Equal((79, 23, true), ((int)saved.X, (int)saved.Y, saved.PendingWrap));
        Assert.True(saved.Protected && saved.Origin);
        Assert.False(state.SavedCursor.Value.Clamp(2000, 2000).PendingWrap);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetCursorPosition(0, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => saved.Clamp(80, 65536));
        byte[] encoded = new byte[23];
        saved.Write(encoded);
        Assert.Equal(saved, GhosttySnapshotSavedCursor.Read(encoded));
        Assert.Throws<InvalidDataException>(() => (saved with { Flags = 128 }).Write(encoded));
        Assert.Throws<ArgumentException>(() => saved.Write(new byte[22]));
    }

    [Fact]
    public void CompleteGoldenScreenRecordsRoundTripExactly()
    {
        byte[] fixture = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        Assert.Equal(fixture, RewriteScreens(fixture, normalize: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveScreenStateAndSavedCursorSurviveManagedRewriting(bool alternate)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native snapshot differential available: {available}");
        if (!available) return;
        using GhosttyTerminal terminal = new(13, 4);
        terminal.Write("\u001b[31;44;4:3m\u001b[1\"q\u001b[3;6H\u001b7\u001b[>3u\u001b[>31u\u001b]133;A;cl=w\u0007\u001b]8;id=cursor;https://example.com\u0007\u001b(0"u8);
        if (alternate) terminal.Write("\u001b[?1049h\u001b[32m\u001b7\u001b[>7u\u001b]8;;https://alternate.example\u0007"u8);
        byte[] encoded = GhosttySnapshot.Encode(terminal);
        byte[] rewritten = RewriteScreens(encoded, normalize: true);
        Assert.Equal(encoded, rewritten);
        using GhosttyTerminal direct = GhosttySnapshot.Decode(encoded);
        using GhosttyTerminal managed = GhosttySnapshot.Decode(rewritten);
        Assert.Equal(GhosttySnapshot.Encode(direct), GhosttySnapshot.Encode(managed));
        // Exercise restored pen, charset, saved cursor and alternate state after installation.
        direct.Write("x\u001b8y\u001b[?1049lz"u8);
        managed.Write("x\u001b8y\u001b[?1049lz"u8);
        Assert.Equal(GhosttySnapshot.Encode(direct), GhosttySnapshot.Encode(managed));
    }

    [Fact]
    public void MalformedScreenSemanticsAndLinksNormalizeExactlyLikeNative()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native snapshot differential available: {available}");
        if (!available) return;
        using GhosttyTerminal terminal = new(13, 4);
        terminal.Write("\u001b7"u8);
        byte[] source = GhosttySnapshot.Encode(terminal);
        Random random = new(721683);
        for (int iteration = 0; iteration < 100; iteration++)
        {
            byte[] malformed = RewriteScreens(source, normalize: false, random);
            using GhosttyTerminal direct = GhosttySnapshot.Decode(malformed);
            using GhosttyTerminal managed = GhosttySnapshot.Decode(RewriteScreens(malformed, normalize: true));
            Assert.Equal(GhosttySnapshot.Encode(direct), GhosttySnapshot.Encode(managed));
        }
    }

    private static byte[] Fixture() => [.. GhosttySnapshotFramingTests.Fixture("screen-header-v1.hex"),
        .. GhosttySnapshotFramingTests.Fixture("screen-saved-cursor-v1.hex"),
        .. GhosttySnapshotFramingTests.Fixture("hyperlink-explicit-v1.hex")];

    private static byte[] Encode(GhosttySnapshotScreenState state)
    {
        using MemoryStream result = new(); state.WritePayloadTo(result); return result.ToArray();
    }

    private static byte[] RewriteScreens(byte[] snapshot, bool normalize, Random? random = null)
    {
        using GhosttySnapshotRecordReader reader = new(snapshot, 1_000_000);
        reader.ReadEnvelope();
        using MemoryStream result = new(); result.Write(GhosttySnapshotFraming.Envelope);
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            if (tag == GhosttySnapshotRecordTag.Screen)
            {
                byte[] screen = payload.ToArray();
                if (random is not null)
                {
                    random.NextBytes(screen.AsSpan(12, 40));
                    random.NextBytes(screen.AsSpan(53, 23));
                    screen[52] = 255; // Nonzero presence must still consume the saved cursor.
                    byte[] link = new byte[random.Next(1, 30)]; random.NextBytes(link); link[0] |= 1;
                    screen = [.. screen.AsSpan(0, 76), .. link];
                }
                if (normalize) screen = Encode(GhosttySnapshotScreenState.Read(screen, 1000, 4096));
                GhosttySnapshotFraming.WriteRecord(result, tag, screen);
            }
            else GhosttySnapshotFraming.WriteRecord(result, tag, payload);
            if (tag == GhosttySnapshotRecordTag.Finish) break;
        }
        return result.ToArray();
    }
}
