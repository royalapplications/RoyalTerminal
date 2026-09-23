// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotPageTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeSnapshotPagesRoundTripThroughManagedCodecs(bool alternate)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native snapshot differential available: {available}");
        if (!available) return;
        using GhosttyTerminal terminal = new(12, 3);
        terminal.SetScrollbackMaxBytes(4 * 1024 * 1024);
        terminal.SetScrollbackMaxLines(200);
        for (int row = 0; row < 80; row++)
            terminal.Write(Encoding.UTF8.GetBytes($"\u001b[38;5;{row}mrow{row}:界a\u0301\r\n"));
        terminal.Write("\u001b]8;id=sample;https://example.com\u001b\\linked\u001b]8;;\u001b\\"u8);
        if (alternate) terminal.Write("\u001b[?1049h\u001b[44mALT"u8);
        byte[] encoded = RewritePages(GhosttySnapshot.Encode(terminal), out int pages);
        Assert.True(pages > 0);
        using GhosttyTerminal restored = GhosttySnapshot.Decode(encoded);
        Assert.Equal(terminal.GetActiveScreen(), restored.GetActiveScreen());
        Assert.Equal((terminal.GetCursorX(), terminal.GetCursorY()), (restored.GetCursorX(), restored.GetCursorY()));
        Assert.Equal(terminal.GetCursorPendingWrap(), restored.GetCursorPendingWrap());
        AssertStyledEqual(terminal, restored);
        if (alternate)
        {
            terminal.Write("\u001b[?1049l"u8);
            restored.Write("\u001b[?1049l"u8);
            AssertStyledEqual(terminal, restored);
        }
    }

    [Fact]
    public void CompleteGoldenSnapshotPagesCanBeReframedWithValidChecksums()
    {
        byte[] fixture = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        byte[] encoded = RewritePages(fixture, out int pages);
        Assert.True(pages > 0);
        // This canonical fixture needs no normalization, including its PAGEs.
        Assert.Equal(fixture, encoded);
    }

    [Fact]
    public void PageMatchesUpstreamGoldenAndOwnsDecodedData()
    {
        byte[] golden = GhosttySnapshotFramingTests.Fixture("page-v1.hex");
        byte[] input = golden.ToArray();
        GhosttySnapshotPage page = GhosttySnapshotPage.Read(input, 6, 64, 1024);
        Array.Fill(input, (byte)0);
        Assert.Equal((3, 2, 2, 2), (page.Grid.Columns, page.Grid.Rows, page.StyleCount, page.HyperlinkCount));
        Assert.True(page.TryGetStyle(1, out GhosttySnapshotStyle bold));
        Assert.Equal(1, bold.Flags);
        Assert.True(page.TryGetStyle(3, out GhosttySnapshotStyle palette));
        Assert.Equal(new GhosttySnapshotColor(1, 42, 0, 0), palette.Background);
        Assert.True(page.TryGetHyperlink(1, out GhosttySnapshotHyperlink first));
        Assert.Equal("alpha"u8.ToArray(), first.Uri.ToArray());
        Assert.Equal("a"u8.ToArray(), first.ExplicitId.ToArray());
        Assert.True(page.TryGetHyperlink(3, out GhosttySnapshotHyperlink second));
        Assert.Equal(0x01020304u, second.ImplicitId);
        Assert.Equal("beta"u8.ToArray(), second.Uri.ToArray());
        using MemoryStream encoded = new();
        page.WritePayloadTo(encoded);
        Assert.Equal(golden, encoded.ToArray());
        page.WritePayloadTo(Stream.Null);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) page.WritePayloadTo(Stream.Null);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void PageRejectsEveryTruncationTrailingBytesAndResourceLimitViolation()
    {
        byte[] golden = GhosttySnapshotFramingTests.Fixture("page-v1.hex");
        for (int length = 0; length < golden.Length; length++)
            Assert.Throws<EndOfStreamException>(() => GhosttySnapshotPage.Read(golden.AsSpan(0, length), 6, 64, 1024));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotPage.Read([.. golden, 0], 6, 64, 1024));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotPage.Read(golden, 5, 64, 1024));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotPage.Read(golden, 6, 1, 1024));
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotPage.Read(golden, 6, 64, 9));
    }

    [Fact]
    public void InvalidFirstEntriesWinOverValidDuplicatesAndMissingIdsBecomeDefaults()
    {
        byte[] style = GhosttySnapshotFramingTests.Fixture("style-v1.hex");
        byte[] invalid = style.ToArray();
        invalid[14] = 1;
        byte[] link = GhosttySnapshotFramingTests.Fixture("hyperlink-implicit-v1.hex");
        byte[] emptyLink = [1, 1, 0, 0, 0, 0, 0, 0, 0];
        byte[] input = Page([(0, style), (5, invalid), (5, style), (9, style)],
            [(0, link), (5, emptyLink), (5, link), (9, link)],
            [(65UL << 2) | (5UL << 26) | (5UL << 48),
             (66UL << 2) | (7UL << 26) | (7UL << 48),
             (67UL << 2) | (9UL << 26) | (9UL << 48)]);
        GhosttySnapshotPage page = GhosttySnapshotPage.Read(input, 3, 0, 1024);
        Assert.Equal((1, 1), (page.StyleCount, page.HyperlinkCount));
        Assert.Equal(65UL << 2, page.Grid.Cells[0]);
        Assert.Equal(66UL << 2, page.Grid.Cells[1]);
        Assert.Equal((67UL << 2) | (9UL << 26) | (9UL << 48) | (1UL << 45), page.Grid.Cells[2]);
        Assert.False(page.TryGetStyle(5, out _));
        Assert.False(page.TryGetHyperlink(5, out _));
        using MemoryStream output = new();
        page.WritePayloadTo(output);
        GhosttySnapshotPage restored = GhosttySnapshotPage.Read(output.ToArray(), 3, 0, 1024);
        Assert.Equal(page.Grid.Cells.ToArray(), restored.Grid.Cells.ToArray());
    }

    [Fact]
    public void CapacityHintsDoNotControlManagedAllocations()
    {
        byte[] bytes = Page([], [], [0]);
        bytes.AsSpan(8, 12).Fill(0xFF);
        GhosttySnapshotPage.Read(bytes, 1, 0, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        GhosttySnapshotPage page = GhosttySnapshotPage.Read(bytes, 1, 0, 0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 4096);
        Assert.Equal(1, page.Grid.Cells.Length);
    }

    [Fact]
    public void EmptyRecordFixtureIsChecksumValidatedBeforePageDecode()
    {
        byte[] record = GhosttySnapshotFramingTests.Fixture("page-empty-record-v1.hex");
        byte[] input = [.. GhosttySnapshotFraming.Envelope, .. record];
        using GhosttySnapshotRecordReader reader = new(input, 1024);
        reader.ReadEnvelope();
        Assert.Equal(GhosttySnapshotRecordTag.Page, reader.ReadRecord(out ReadOnlySpan<byte> payload));
        GhosttySnapshotPage page = GhosttySnapshotPage.Read(payload, 1024, 0, 0);
        Assert.All(page.Grid.Cells.ToArray(), cell => Assert.Equal(0UL, cell));
    }

    [Fact]
    public void InvalidHyperlinkKindIsFatalEvenForAnIgnoredId()
    {
        byte[] input = Page([], [(0, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0 })], [0]);
        Assert.Throws<InvalidDataException>(() => GhosttySnapshotPage.Read(input, 1, 0, 0));
    }

    private static byte[] Page((ushort Id, byte[] Value)[] styles, (ushort Id, byte[] Value)[] links, ulong[] cells)
    {
        using MemoryStream output = new();
        using BinaryWriter writer = new(output);
        writer.Write((ushort)cells.Length); writer.Write((ushort)1);
        writer.Write((ushort)styles.Length); writer.Write((ushort)links.Length);
        writer.Write(new byte[12]);
        foreach ((ushort id, byte[] value) in styles) { writer.Write(id); writer.Write(value); }
        foreach ((ushort id, byte[] value) in links) { writer.Write(id); writer.Write(value); }
        writer.Write((byte)0x30); writer.Write((ushort)cells.Length);
        foreach (ulong cell in cells) writer.Write(cell);
        writer.Write(0u);
        return output.ToArray();
    }

    private static byte[] RewritePages(byte[] snapshot, out int pages)
    {
        using GhosttySnapshotRecordReader reader = new(snapshot, 16 * 1024 * 1024);
        reader.ReadEnvelope();
        using MemoryStream result = new();
        result.Write(GhosttySnapshotFraming.Envelope);
        pages = 0;
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            if (tag == GhosttySnapshotRecordTag.Page)
            {
                GhosttySnapshotPage page = GhosttySnapshotPage.Read(payload, 1_000_000, 1_000_000, 4 * 1024 * 1024);
                using MemoryStream rewritten = new();
                page.WritePayloadTo(rewritten);
                GhosttySnapshotFraming.WriteRecord(result, tag, rewritten.GetBuffer().AsSpan(0, (int)rewritten.Length));
                pages++;
            }
            else GhosttySnapshotFraming.WriteRecord(result, tag, payload);
            if (tag == GhosttySnapshotRecordTag.Finish) break;
        }
        Assert.Equal(snapshot.Length, reader.SourceOffset);
        return result.ToArray();
    }

    private static void AssertStyledEqual(GhosttyTerminal expected, GhosttyTerminal actual)
    {
        GhosttyFormatterOptions options = new(Format: GhosttyVtNative.GhosttyFormatterFormat.Vt,
            Extra: new(IncludeModes: true, IncludeScrollingRegion: true,
                Screen: new(IncludeCursor: true, IncludeStyle: true, IncludeHyperlinks: true, IncludeProtection: true)));
        using GhosttyFormatter first = new(expected, options), second = new(actual, options);
        Assert.Equal(first.FormatToString(), second.FormatToString());
    }
}
