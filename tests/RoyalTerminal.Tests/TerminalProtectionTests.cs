// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalProtectionTests(ITestOutputHelper output)
{
    [Fact]
    public void PackedProtectionIsIndependentOfWideSpacerAndDoesNotGrowCells()
    {
        Assert.True(Unsafe.SizeOf<TerminalCell>() <= 48);
        TerminalCell cell = new() { IsProtected = true, IsWideSpacerHead = true };
        cell.IsWideSpacerHead = false;
        Assert.True(cell.IsProtected);
        cell.IsWideSpacerHead = true;
        cell.IsProtected = false;
        Assert.True(cell.IsWideSpacerHead);
        Assert.False(TerminalCell.Empty().IsProtected);
    }

    [Theory]
    [InlineData("\u001b[1\"qA\u001b[0mB\u001b[0\"qC\u001b[2\"qD")]
    [InlineData("\u001b[1\"qA\u001b[3\"qB\u001b[0;1\"qC")]
    [InlineData("\u001bVA\u001bWB\u001b[1\"qC\u001b[2\"qD")]
    [InlineData("\u001b[1\"q\u001b7\u001b[0\"qA\u001b8B")]
    [InlineData("\u001bVA\u001b[?47hB\u001bWC\u001b[?47lD")]
    [InlineData("\u001b[1\"qA\u001b[?1049h\u001b[0\"qB\u001b[?1049lC")]
    [InlineData("AB\u001b[?1049hC")]
    [InlineData("AB\u001b[?1047hC\u001b[?1047lD\u001b[?47h")]
    [InlineData("\u001b[?47h\u001bVA\u001bWB\u001b[?47l\u001b[?1049h")]
    [InlineData("\u001b[?47h\u001bVA\u001bWB\u001b[?1047l\u001b[?47h")]
    [InlineData("\u001b[1\"qABC\u001b[H\u001b[?2KZ")]
    [InlineData("\u001b[1\"qAB\u001b[0\"qCD\u001b[H\u001b[2KZ")]
    [InlineData("\u001bVAB\u001bWCD\u001b[H\u001b[2KZ")]
    [InlineData("\u001bVAB\u001bWCD\u001b[1\"q\u001b[0\"q\u001b[H\u001b[2KZ")]
    [InlineData("\u001b[1\"q界\u001b[0\"qAB\u001b[1;2H\u001b[?K")]
    [InlineData("\u001b[1\"q界\u001b[0\"qAB\u001b[1;1H\u001b[?1K")]
    [InlineData("\u001bV界\u001bWAB\u001b[1;2H\u001b[X")]
    [InlineData("\u001bV界\u001bWAB\u001b[H\u001b[X")]
    [InlineData("\u001b[1\"qA\u001b[0\"qB\u001b[H\u001b[X")]
    [InlineData("\u001bVA\u001bWB\u001b[H\u001b[2X")]
    [InlineData("\u001b[1\"qABC\u001b[0\"q\u001b[H\u001b[@\u001b[2P")]
    [InlineData("\u001bVA\u001b7\u001bWB\u001b[!p\u001b8C")]
    [InlineData("\u001bVA\u001bcB\u001b[H\u001b[?2J")]
    public void ProtectionAndSubsequentPrintingMatchNativeAtEverySplit(string input)
    {
        if (!Available()) return;
        CompareEverySplit(input);
    }

    [Theory]
    [InlineData("\u001b[1\"q", "\u001b[0\"q", "J")]
    [InlineData("\u001bV", "\u001bW", "J")]
    [InlineData("\u001b[1\"q", "\u001b[0\"q", "K")]
    [InlineData("\u001bV", "\u001bW", "K")]
    public void AllEraseModesAndCursorBoundariesMatchNative(string protect, string unprotect, string command)
    {
        if (!Available()) return;
        string prefix = protect + "AB" + unprotect + "CD\r\n" + protect + "EF" + unprotect + "GH\r\n" + protect + "IJ" + unprotect + "KL";
        foreach (string selective in new[] { "", "?" })
        for (int mode = 0; mode <= 2; mode++)
        for (int row = 1; row <= 3; row++)
        for (int column = 1; column <= 4; column++)
            CompareEverySplit(prefix + $"\u001b[{row};{column}H\u001b[{selective}{mode}{command}", splits: false);
    }

    [Fact]
    public void WideAndWrappedProtectionEdgesMatchNative()
    {
        if (!Available()) return;
        foreach (string prefix in new[] { "\u001bV界AB界", "\u001b[1\"q界AB界", "\u001bV1234567界AB", "\u001b[1\"q1234567界AB" })
        foreach (string command in new[] { "K", "?K", "1K", "?1K", "2K", "?2K", "X", "2X", "J", "?J", "2J", "?2J" })
        for (int row = 1; row <= 2; row++)
        for (int column = 1; column <= 8; column++)
            CompareEverySplit(prefix + $"\u001bW\u001b[{row};{column}H\u001b[{command}", splits: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectiveErasePreservesProtectedStyleLinkAndClusterButUsesCurrentEraseBackground(bool native)
    {
        if (native && !Available()) return;
        TerminalScreen screen = new(8, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[1\"q\u001b[1;38;5;3;48;5;4m\u001b]8;;https://example.com\u001b\\A\u0301\u001b]8;;\u001b\\\u001b[0\"qB\u001b[0;48;5;5m\u001b[H\u001b[?2K"));
        TerminalCell protectedCell = screen.GetViewportRow(0)[0], erased = screen.GetViewportRow(0)[1];
        Assert.True(protectedCell.IsProtected);
        Assert.Equal("A\u0301", protectedCell.Grapheme);
        Assert.Equal(CellAttributes.Bold, protectedCell.Attributes);
        Assert.Equal(TerminalColorIdentity.Palette(4), protectedCell.BackgroundIdentity);
        Assert.True(screen.TryGetHyperlinkUrl(protectedCell.HyperlinkId, out string? url));
        Assert.Equal("https://example.com", url);
        Assert.False(erased.IsProtected);
        Assert.False(erased.HasContent);
        Assert.Equal(CellAttributes.None, erased.Attributes);
        Assert.Equal(0, erased.HyperlinkId);
        Assert.Equal(TerminalColorIdentity.Palette(5), erased.BackgroundIdentity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeldChangesPublishProtectionAtomically(bool native)
    {
        if (native && !Available()) return;
        TerminalScreen screen = new(8, 3);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("\u001b[1\"qA\u001b[?2026h\r\u001b[0\"qB"u8);
        Assert.True(screen.GetViewportRow(0)[0].IsProtected);
        Assert.Equal('A', screen.GetViewportRow(0)[0].Codepoint);
        processor.Process("\u001b[?2026l"u8);
        Assert.False(screen.GetViewportRow(0)[0].IsProtected);
        Assert.Equal('B', screen.GetViewportRow(0)[0].Codepoint);
    }

    [Fact]
    public void ReflowAndCopyOnWritePreserveProtection()
    {
        TerminalScreen screen = new(8, 3);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[1\"qAB界CD"));
        TerminalScreen copy = screen.CreateStateCopy();
        processor.Process("\r\u001b[0\"qX"u8);
        Assert.True(copy.GetViewportRow(0)[0].IsProtected);
        copy.Resize(4, 3);
        int protectedText = 0;
        for (int row = 0; row < copy.TotalRows; row++)
        foreach (TerminalCell cell in copy.GetRow(row).ReadOnlyCells)
            if (cell.HasContent) { Assert.True(cell.IsProtected); protectedText++; }
        Assert.Equal(5, protectedText);
    }

    [Fact]
    public void WarmSelectiveEraseDoesNotAllocate()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(80, 24));
        processor.Process("\u001b[1\"qprotected\u001b[0\"q\r"u8);
        for (int i = 0; i < 100; i++) processor.Process("\u001b[?2K"u8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) processor.Process("\u001b[?2K"u8);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void NativeHistorySnapshotsRetainOwnedProtectionAfterTerminalReset()
    {
        if (!Available()) return;
        using GhosttyVtProcessor processor = new(new TerminalScreen(8, 2, 16));
        processor.Process("\u001b[1\"qA\u001b[0\"qB\r\nnext\r\nlast"u8);
        Assert.True(processor.TryCreateScreenSnapshot(0, 1, 0, out TerminalScreen snapshot));
        Assert.True(snapshot.GetViewportRow(0)[0].IsProtected);
        Assert.False(snapshot.GetViewportRow(0)[1].IsProtected);
        processor.Process("\u001bc"u8);
        Assert.Equal('A', snapshot.GetViewportRow(0)[0].Codepoint);
        Assert.True(snapshot.GetViewportRow(0)[0].IsProtected);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native protection differential available: {available}");
        return available;
    }

    private static void CompareEverySplit(string input, bool splits = true)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        TerminalScreen expected = new(8, 3);
        using GhosttyVtProcessor native = new(expected);
        native.Process(bytes);
        for (int split = 0; split <= bytes.Length; split += splits ? 1 : bytes.Length)
        {
            TerminalScreen actual = new(8, 3);
            using BasicVtProcessor managed = new(actual);
            managed.Process(bytes.AsSpan(0, split));
            managed.Process(bytes.AsSpan(split));
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 8; column++)
            {
                TerminalCell e = expected.GetViewportRow(row)[column], a = actual.GetViewportRow(row)[column];
                Assert.True((e.Codepoint, e.Width, e.IsProtected) == (a.Codepoint, a.Width, a.IsProtected),
                    $"Input {Convert.ToHexString(bytes)}, split {split}, cell {row},{column}: expected {e.Codepoint}/{e.Width}/{e.IsProtected}, actual {a.Codepoint}/{a.Width}/{a.IsProtected}");
            }
        }
    }
}
