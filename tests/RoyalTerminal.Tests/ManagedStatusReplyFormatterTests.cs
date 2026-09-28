// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedStatusReplyFormatterTests
{
    [Fact]
    public void FullPenUsesNativeOrderingAndOmitsUnderlineColor()
    {
        TerminalCell pen = new()
        {
            Attributes = CellAttributes.Bold | CellAttributes.Dim | CellAttributes.Italic | CellAttributes.Blink |
                CellAttributes.Inverse | CellAttributes.Hidden | CellAttributes.Strikethrough,
            UnderlineStyle = TerminalUnderlineStyle.Dashed, Decorations = CellDecorations.Overline,
            ForegroundIdentity = TerminalColorIdentity.Rgb(0xFFFFFF), BackgroundIdentity = TerminalColorIdentity.Rgb(0x010203),
            HasUnderlineColor = true, UnderlineIdentity = TerminalColorIdentity.Palette(255),
        };
        Span<byte> buffer = stackalloc byte[ManagedStatusReplyFormatter.MaximumBytes];
        int written = ManagedStatusReplyFormatter.Sgr(buffer, in pen);
        Assert.Equal("\u001bP1$r0;1;2;3;4:5;53;5;7;8;9;38:2::255:255:255;48:2::1:2:3m\u001b\\",
            Encoding.ASCII.GetString(buffer[..written]));
    }

    [Fact]
    public void AllPaletteIndexesKeepCompactAnsiAndExtendedColorIdentity()
    {
        Span<byte> buffer = stackalloc byte[ManagedStatusReplyFormatter.MaximumBytes];
        for (int index = 0; index < 256; index++)
        {
            TerminalCell pen = new()
            {
                ForegroundIdentity = TerminalColorIdentity.Palette((byte)index),
                BackgroundIdentity = TerminalColorIdentity.Palette((byte)index),
            };
            int length = ManagedStatusReplyFormatter.Sgr(buffer, in pen);
            string foreground = index < 8 ? $"{30 + index}" : index < 16 ? $"{82 + index}" : $"38:5:{index}";
            string background = index < 8 ? $"{40 + index}" : index < 16 ? $"{92 + index}" : $"48:5:{index}";
            Assert.Equal($"\u001bP1$r0;{foreground};{background}m\u001b\\", Encoding.ASCII.GetString(buffer[..length]));
        }
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, ";4")]
    [InlineData(2, ";4:2")]
    [InlineData(3, ";4:3")]
    [InlineData(4, ";4:4")]
    [InlineData(5, ";4:5")]
    public void EveryUnderlineStyleHasExactNativeSpelling(int style, string expected)
    {
        TerminalCell pen = new() { UnderlineStyle = (TerminalUnderlineStyle)style };
        Span<byte> buffer = stackalloc byte[ManagedStatusReplyFormatter.MaximumBytes];
        int length = ManagedStatusReplyFormatter.Sgr(buffer, in pen);
        Assert.Equal($"\u001bP1$r0{expected}m\u001b\\", Encoding.ASCII.GetString(buffer[..length]));
        if (style == 0)
        {
            pen.Attributes = CellAttributes.Underline;
            length = ManagedStatusReplyFormatter.Sgr(buffer, in pen);
            Assert.Equal("\u001bP1$r0;4m\u001b\\", Encoding.ASCII.GetString(buffer[..length]));
        }
    }

    [Fact]
    public void NumberFormattingIsCultureIndependentAndAcceptsFullManagedGeometry()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Span<byte> buffer = stackalloc byte[ManagedStatusReplyFormatter.MaximumBytes];
            int length = ManagedStatusReplyFormatter.Margins(buffer, 1, int.MaxValue, horizontal: true);
            Assert.Equal("\u001bP1$r1;2147483647s\u001b\\", Encoding.ASCII.GetString(buffer[..length]));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void BoundedFormattingHasNoHeapScratch()
    {
        TerminalCell pen = new() { ForegroundIdentity = TerminalColorIdentity.Palette(255) };
        Span<byte> buffer = stackalloc byte[ManagedStatusReplyFormatter.MaximumBytes];
        for (int i = 0; i < 100; i++) _ = ManagedStatusReplyFormatter.Sgr(buffer, in pen);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) _ = ManagedStatusReplyFormatter.Sgr(buffer, in pen);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("", "m", "0m")]
    [InlineData("\u001b[1;2;3;4:3;53;5;7;8;9;38;5;255;48;2;1;2;3m", "m", "0;1;2;3;4:3;53;5;7;8;9;38:5:255;48:2::1:2:3m")]
    [InlineData("\u001b[21;58;5;111m", "m", "0;4:2m")]
    [InlineData("\u001b[31;104m", "m", "0;31;104m")]
    [InlineData("\u001b[2;7r", "r", "2;7r")]
    [InlineData("\u001b[?69h\u001b[3;25s", "s", "3;25s")]
    [InlineData("", "s", null)]
    [InlineData("\u001b[1 q", " q", "1 q")]
    [InlineData("\u001b[2 q", " q", "2 q")]
    [InlineData("\u001b[3 q", " q", "3 q")]
    [InlineData("\u001b[4 q", " q", "4 q")]
    [InlineData("\u001b[5 q", " q", "5 q")]
    [InlineData("\u001b[6 q", " q", "6 q")]
    [InlineData("", "\"q", null)]
    [InlineData("", "", null)]
    public void StreamingRepliesMatchNativeSubsetExactly(string setup, string request, string? expected)
    {
        byte[] query = Encoding.ASCII.GetBytes($"\u001bP$q{request}\u001b\\");
        byte[] wire = Encoding.ASCII.GetBytes(expected is null ? "\u001bP0$r\u001b\\" : $"\u001bP1$r{expected}\u001b\\");
        using BasicVtProcessor processor = new(new TerminalScreen(32, 8));
        processor.Process(Encoding.ASCII.GetBytes(setup));
        List<byte[]> replies = [];
        processor.ResponseCallback = replies.Add;
        foreach (byte value in query) processor.Process(new byte[] { value });
        Assert.Equal(wire, Assert.Single(replies));
        // Callback arrays are owned, not aliases of reused stack/parser storage.
        replies[0][0] = 0;
        replies.Clear();
        processor.Process(query);
        Assert.Equal(wire, Assert.Single(replies));
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(32, 8));
        List<byte[]> nativeReplies = [];
        native.ResponseCallback = nativeReplies.Add;
        native.Process(Encoding.ASCII.GetBytes(setup));
        native.Process(query);
        Assert.Equal(wire, Assert.Single(nativeReplies));
    }

    [Fact]
    public void QueriesWithoutACallbackConsumeParserStateAndOversizedRequestsStaySilent()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2));
        processor.Process("\u001bP$qm\u001b\\"u8);
        List<byte[]> replies = [];
        processor.ResponseCallback = replies.Add;
        processor.Process("\u001bP$qabc\u001b\\\u001bP$qm\u001b\\"u8);
        Assert.Equal("\u001bP1$r0m\u001b\\"u8.ToArray(), Assert.Single(replies));
    }
}
