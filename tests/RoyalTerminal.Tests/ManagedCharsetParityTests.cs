// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedCharsetParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("\u001b(0_0`abcdefghijklmnopqrstuvwxyz{|}~\u001b(Bq")]
    [InlineData("\u001b(A#qéÿ界")]
    [InlineData("\u001b)A\u000e##\u000f#")]
    [InlineData("\u001b*0\u001bNqq")]
    [InlineData("\u001b+A\u001bO##")]
    [InlineData("\u001b*0\u001bnqq\u001b+A\u001bo##\u000fq#")]
    [InlineData("\u001b(0\u001b(Cq\u001b(Bq")]
    [InlineData("\u001b(0\u001b-N\u001b.A\u001b/0q")]
    [InlineData("\u001b*0\u001bN\u0007\r\nqQ")]
    [InlineData("\u001b*A\u001bNé##")]
    [InlineData("\u001b*0\u001bN界q")]
    [InlineData("\u001b*0\u001bN\u001b7\u001bNq\u001b8qq")]
    [InlineData("\u001b(0\u001b7\u001b(B\u001b)A\u000e\u001b8q#\u000e#")]
    [InlineData("\u001b(0\u001b~éÿ界\u001b|q")]
    [InlineData("\u001b*0\u001bN\u001bcq")]
    [InlineData("\u001b(0q\u001b[3b")]
    [InlineData("\u001b*0\u001bNq\u001b[3b")]
    [InlineData("A\u001b*0\u001bN\u0301qq")]
    [InlineData("\u001b(0界\u001b(B界")]
    [InlineData("\u001b)0\u000e\u001b[?1049hqq\u001b[?1049lqq")]
    [InlineData("\u001b*0\u001bn\u001b[s\u000f\u001b[uqq")]
    [InlineData("\u001b[1;40H\u001b*A\u001bN界#")]
    [InlineData("\u001b[?7l\u001b[1;40H\u001b*A\u001bN界#")]
    [InlineData("\u001b*0\u001bN\u001b[?2026hqq\u001b[?2026l")]
    public void CharsetStateAndPrintingMatchNativeAtEveryByteSplit(string input)
    {
        if (!Available()) return;
        Compare(input);
    }

    [Fact]
    public void EveryPrintableByteInEveryDesignatedSlotMatchesNative()
    {
        if (!Available()) return;
        foreach (char designation in "BA0")
        for (int slot = 0; slot < 4; slot++)
        {
            string invoke = slot switch { 0 => "\u000f", 1 => "\u000e", 2 => "\u001bn", _ => "\u001bo" };
            for (int cp = 0x20; cp <= 255; cp++)
            {
                if (cp is >= 0x80 and <= 0x9F) continue;
                Compare("\u001b" + "()*+"[slot] + designation + invoke + (char)cp, everySplit: false);
            }
        }
    }

    [Theory]
    [InlineData("\u001b8X")]
    [InlineData("\u001b[31m\u001b7\u001b[?1049h\u001b[32m\u001b7\u001b[?1049lX")]
    [InlineData("\u001b[31m\u001b7\u001b[?47h\u001b[32m\u001b7\u001b[34m\u001b8X\u001b[?47l\u001b8Y")]
    [InlineData("\u001b[2;3r\u001b[?6h\u001b[2;3H\u001b7\u001b[?6l\u001b8X")]
    [InlineData("\u001b]8;;https://old.example\u001b\\\u001b7\u001b]8;;https://new.example\u001b\\\u001b8X")]
    [InlineData("\u001b[31m\u001b[s\u001b[32m\u001b[uX")]
    [InlineData("\u001b[31m\u001b7\u001bc\u001b8X")]
    public void SavedPensAreIndependentAndRestoreNativeState(string input)
    {
        if (!Available()) return;
        Compare(input);
    }

    [Fact]
    public void PackedStateMatchesNativeSnapshotRegistryIncludingGrAndPendingShift()
    {
        if (!Available()) return;
        ManagedCharsetState state = new();
        using GhosttyTerminal native = new(8, 3);
        Assert.Equal(ReadCharset(native), state.Bits);
        for (int slot = 0; slot < 4; slot++)
        {
            state.Designate(slot, (byte)"BA00"[slot]);
            native.Write(Encoding.ASCII.GetBytes("\u001b" + "()*+"[slot] + "BA00"[slot]));
            Assert.Equal(ReadCharset(native), state.Bits);
        }
        state.Invoke(3); native.Write("\u001bo"u8);
        state.Invoke(1, right: true); native.Write("\u001b~"u8);
        state.Invoke(2, single: true); native.Write("\u001bN"u8);
        Assert.Equal(ReadCharset(native), state.Bits);
        _ = state.MapPrintedCell('q'); native.Write("q"u8);
        Assert.Equal(ReadCharset(native), state.Bits);
    }

    [Fact]
    public void StyledExportRestoresAllDesignationsAndPendingSingleShift()
    {
        if (!Available()) return;
        using BasicVtProcessor source = new(new TerminalScreen(8, 3));
        source.Process("\u001b(0\u001b)A\u001b*0\u001b+A\u001bn\u001b|\u001bO"u8);
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(Extras: new(IncludeCharsets: true)), out string exported));
        Assert.Contains("\u001b*0", exported);
        Assert.Contains("\u001b+A", exported);
        Assert.Contains("\u001bO", exported);
        Compare(exported + "\u001b[H#q\u000e#\u000fq");
    }

    [Fact]
    public void SavedLogicalColorsResolveAgainstTheCurrentThemeWithoutAllocations()
    {
        TerminalScreen screen = new(8, 3);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[38;5;1m\u001b7"u8);
        processor.ApplyTheme(screen.Theme.WithPaletteColor(1, 0xFF123456));
        processor.Process("\u001b8X"u8);
        Assert.Equal(0xFF123456u, screen.GetViewportRow(0)[0].Foreground);
        for (int i = 0; i < 100; i++) processor.Process("\u001b7\u001b8"u8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) processor.Process("\u001b7\u001b8"u8);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void StyledExportSelectsAlternateBeforeDrawingAndRetainsText()
    {
        using BasicVtProcessor source = new(new TerminalScreen(8, 3));
        source.Process("\u001b[?1049hALT"u8);
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(TrimTrailingWhitespace: true, Extras: new(IncludeModes: true, IncludeCursor: true)), out string exported));
        TerminalScreen screen = new(8, 3);
        using BasicVtProcessor target = new(screen);
        target.Process(Encoding.UTF8.GetBytes(exported));
        Assert.True(target.AlternateScreen);
        Assert.Equal('A', screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal('L', screen.GetViewportRow(0)[1].Codepoint);
        Assert.Equal('T', screen.GetViewportRow(0)[2].Codepoint);
        Assert.Equal((source.CursorCol, source.CursorRow), (target.CursorCol, target.CursorRow));
    }

    private static ushort ReadCharset(GhosttyTerminal terminal)
    {
        using MemoryStream stream = new(GhosttySnapshot.Encode(terminal));
        using GhosttySnapshotRecordReader reader = new(stream, 1_000_000);
        reader.ReadEnvelope();
        while (true)
        {
            GhosttySnapshotRecordTag tag = reader.ReadRecord(out ReadOnlySpan<byte> payload);
            if (tag == GhosttySnapshotRecordTag.Screen) return GhosttySnapshotScreenState.Read(payload, 1000, 1000).Charset.Bits;
            Assert.NotEqual(GhosttySnapshotRecordTag.Finish, tag);
        }
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native charset differential available: {available}");
        return available;
    }

    private static void Compare(string input, bool everySplit = true)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        TerminalScreen expected = new(40, 3);
        using GhosttyVtProcessor native = new(expected);
        native.Process(bytes);
        for (int split = 0; split <= bytes.Length; split += everySplit ? 1 : Math.Max(1, bytes.Length))
        {
            TerminalScreen actual = new(40, 3);
            using BasicVtProcessor managed = new(actual);
            managed.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(split));
            Assert.Equal((native.CursorCol, native.CursorRow), (managed.CursorCol, managed.CursorRow));
            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 40; col++)
            {
                TerminalCell a = actual.GetViewportRow(row)[col], e = expected.GetViewportRow(row)[col];
                Assert.True((e.Codepoint, e.Width, e.IsProtected, e.Grapheme) == (a.Codepoint, a.Width, a.IsProtected, a.Grapheme),
                    $"Input {Convert.ToHexString(bytes)}, split {split}, cell {row},{col}: expected {e.Codepoint}/{e.Width}/{e.Grapheme}, actual {a.Codepoint}/{a.Width}/{a.Grapheme}");
                if (e.HasContent && e.Width != 0)
                {
                    Assert.Equal(e.ForegroundIdentity, a.ForegroundIdentity);
                    Assert.Equal(e.Attributes, a.Attributes);
                    expected.TryGetHyperlinkUrl(e.HyperlinkId, out string? eUrl);
                    actual.TryGetHyperlinkUrl(a.HyperlinkId, out string? aUrl);
                    Assert.Equal(eUrl, aUrl);
                }
            }
        }
    }
}
