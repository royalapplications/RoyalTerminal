// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>Behavior regressions derived from the pinned Ghostty terminal changes.</summary>
public sealed class ManagedGhosttyRegressionTests
{
    [Theory]
    [InlineData("?65535", "?65535;0")]
    [InlineData("?32768", "?32768;0")]
    [InlineData("65535", "65535;0")]
    public void ModeQueriesPreserveFullUnsignedSixteenBitNumbers(string query, string reply)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        string? response = null;
        processor.ResponseCallback = bytes => response = Encoding.ASCII.GetString(bytes);
        processor.Process(Encoding.ASCII.GetBytes($"\u001b[{query}$p"));
        Assert.Equal($"\u001b[{reply}$y", response);
    }

    [Fact]
    public void NarrowingWideGraphemeAtRightEdgeClearsPendingWrap()
    {
        TerminalScreen screen = new(2, 2, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[?2027h☔\uFE0EB"));
        Assert.Equal(1, screen.GetViewportRow(0)[0].Width);
        Assert.Equal('B', screen.GetViewportRow(0)[1].Codepoint);
        Assert.Equal(0, processor.CursorRow);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(1000)]
    [InlineData(1004)]
    [InlineData(2026)]
    [InlineData(2027)]
    [InlineData(2031)]
    public void UnrelatedDecModesDoNotEmitVisibilityReports(int mode)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        List<byte[]> replies = [];
        processor.ResponseCallback = replies.Add;
        processor.Process(Encoding.ASCII.GetBytes($"\u001b[?{mode}h"));
        Assert.Empty(replies);
    }

    [Fact]
    public void FullResetAlwaysRemovesHostProgress()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        List<TerminalProgressReport> reports = [];
        processor.ProgressReportCallback = reports.Add;
        processor.Process("\u001b]9;4;1;50\u001b\\\u001bc\u001bc"u8);
        Assert.Equal(new[]
        {
            new TerminalProgressReport(TerminalProgressState.Set, 50),
            new TerminalProgressReport(TerminalProgressState.Remove, null),
            new TerminalProgressReport(TerminalProgressState.Remove, null),
        }, reports);
    }

    [Fact]
    public void ClipboardReadsFollowRequestOrderAndBoundTheListingPacket()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        string? response = null;
        processor.ResponseCallback = bytes => response = Encoding.ASCII.GetString(bytes);
        processor.ClipboardReadCallback = _ => new(TerminalClipboardReadResult.Success,
            [new("b", "B"u8.ToArray()), new("a", "A"u8.ToArray()), new("a", "duplicate"u8.ToArray())],
            Enumerable.Range(0, 32).Select(index => $"application/{index:D2}" + new string('x', 256)).ToArray());
        string requested = Convert.ToBase64String(". a b"u8);
        processor.Process(Encoding.ASCII.GetBytes($"\u001b]5522;type=read;{requested}\u001b\\"));

        Assert.NotNull(response);
        string[] packets = response.Split("\u001b\\", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, packets.Length);
        Assert.EndsWith(":mime=YQ==;QQ==", packets[2], StringComparison.Ordinal);
        Assert.EndsWith(":mime=Yg==;Qg==", packets[3], StringComparison.Ordinal);
        string listing = packets[1][(packets[1].LastIndexOf(';') + 1)..];
        byte[] decodedListing = Convert.FromBase64String(listing);
        Assert.InRange(decodedListing.Length, 1, 4096);
        Assert.Equal((byte)'\n', decodedListing[^1]);
    }

    [Fact]
    public void Utf8ControlStringsPreserveC1ContinuationBytesAcrossChunks()
    {
        TerminalScreen screen = new(16, 2, 0);
        using BasicVtProcessor processor = new(screen);
        string? title = null;
        processor.TitleCallback = value => title = value;

        // Ghostty 4e817e79a treats DCS high bytes as payload. Windows
        // Terminal and xterm.js parse decoded characters, so Ü cannot act as ST.
        byte[] input = Encoding.UTF8.GetBytes("\u001b]2;ÜÛ\u001b\\\u001bPqÜÛ[31m\u001b\\X");
        foreach (byte value in input)
        {
            processor.Process([value]);
        }

        Assert.Equal("ÜÛ", title);
        Assert.Equal('X', screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal(1, processor.CursorCol);
    }

    [Fact]
    public void C0ControlsAreIgnoredButGroundDeleteIsPrintedLikeGhostty()
    {
        TerminalScreen screen = new(16, 2, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process([0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
            0x18, 0x19, 0x1A, 0x1C, 0x1D, 0x1E, 0x1F, 0x7F, (byte)'X']);
        Assert.Equal(0x7F, screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal('X', screen.GetViewportRow(0)[1].Codepoint);
        Assert.Equal(2, processor.CursorCol);
    }

    [Fact]
    public void GraphemeSuffixesAreBoundedWithoutAdvancingTheCursor()
    {
        TerminalScreen screen = new(8, 2, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("A" + new string('\u0301', 512) + "B"));

        Assert.Equal("A" + new string('\u0301', 64), screen.GetViewportRow(0)[0].Grapheme);
        Assert.Equal('B', screen.GetViewportRow(0)[1].Codepoint);
        Assert.Equal(2, processor.CursorCol);
    }

    [Fact]
    public void EraseCompleteLineClearsSoftWrapBeforeResize()
    {
        TerminalScreen screen = new(5, 5, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process("ABCDE123\u001b[H\u001b[2KX"u8);

        Assert.False(screen.GetViewportRow(0).WrapsToNext);
        processor.ResizeScreen(10, 5, 0, 0, reflowOnResize: true);
        Assert.Equal('X', screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal('1', screen.GetViewportRow(1)[0].Codepoint);
    }

    [Fact]
    public void AllocatingClipboardOscAcceptsPayloadsLargerThanTheOldFourKiBLimit()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        string? clipboard = null;
        processor.ClipboardWriteCallback = (_, value) => clipboard = value;
        string expected = new('a', 16_384);
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(expected));
        processor.Process(Encoding.UTF8.GetBytes($"\u001b]52;c;{encoded}\u0007"));
        Assert.Equal(expected, clipboard);
    }

    [Fact]
    public void FixedTitleOscRejectsOversizedPayloadWithoutChangingPriorTitle()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        processor.SetTitle("previous"u8);
        int callbacks = 0;
        processor.TitleCallback = _ => callbacks++;
        processor.Process(Encoding.UTF8.GetBytes($"\u001b]2;{new string('a', 16_384)}\u0007"));
        byte[] title = new byte[8];
        Assert.True(processor.TryCopyTitle(title, out int length));
        Assert.Equal("previous"u8.ToArray(), title); Assert.Equal(8, length); Assert.Equal(0, callbacks);
    }

    [Fact]
    public void ClipboardWriteLimitRejectsWholeTransactionBeforeDelivery()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0),
            new BasicVtProcessorOptions { ClipboardWriteLimitBytes = 5 });
        int writes = 0;
        string? response = null;
        processor.ClipboardWriteRequestCallback = _ =>
        {
            writes++;
            return new(TerminalClipboardWriteResult.Success);
        };
        processor.ResponseCallback = bytes => response = Encoding.ASCII.GetString(bytes);

        processor.Process("\u001b]5522;type=write:id=limit\u001b\\"u8);
        processor.Process("\u001b]5522;type=wdata:mime=dGV4dC9wbGFpbg==;SGVsbG8h\u001b\\"u8);
        processor.Process("\u001b]5522;type=wdata\u001b\\"u8);

        Assert.Equal(0, writes);
        Assert.Equal("\u001b]5522;type=write:status=EFBIG:id=limit\u001b\\", response);
    }

    [Fact]
    public void ClipboardSplitBase64AndIndependentPaddedChunksAreDecoded()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        TerminalClipboardWrite? write = null;
        processor.ClipboardWriteRequestCallback = value =>
        {
            write = value;
            return new(TerminalClipboardWriteResult.Success);
        };
        string payload = new('x', 16_384);
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

        processor.Process("\u001b]5522;type=write\u001b\\"u8);
        foreach (string chunk in new[] { encoded[..1], encoded[1..7], encoded[7..], "IQ==" })
        {
            processor.Process(Encoding.ASCII.GetBytes(
                $"\u001b]5522;type=wdata:mime=dGV4dC9wbGFpbg==;{chunk}\u001b\\"));
        }
        processor.Process("\u001b]5522;type=wdata\u001b\\"u8);

        Assert.NotNull(write);
        Assert.Equal(payload + "!", Encoding.UTF8.GetString(Assert.Single(write.Contents).Data));
    }

    [Fact]
    public void MalformedMetadataDoesNotAbortAnExistingClipboardTransaction()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        TerminalClipboardWrite? write = null;
        processor.ClipboardWriteRequestCallback = value =>
        {
            write = value;
            return new(TerminalClipboardWriteResult.Success);
        };
        processor.Process("\u001b]5522;type=write\u001b\\"u8);
        processor.Process("\u001b]5522;type=wdata:malformed\u001b\\"u8);
        processor.Process("\u001b]5522;type=wdata:mime=dGV4dC9wbGFpbg==;SGk=\u001b\\"u8);
        processor.Process("\u001b]5522;type=wdata\u001b\\"u8);
        Assert.NotNull(write);
        Assert.Equal("Hi", Encoding.UTF8.GetString(Assert.Single(write.Contents).Data));
    }

    [Fact]
    public void OverlongValidClipboardPasswordIsTreatedAsAbsent()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2, 0));
        TerminalClipboardWrite? write = null;
        processor.ClipboardWriteRequestCallback = value =>
        {
            write = value;
            return new(TerminalClipboardWriteResult.Success);
        };
        string password = Convert.ToBase64String(Encoding.ASCII.GetBytes(new string('x', 129)));
        processor.Process(Encoding.ASCII.GetBytes(
            $"\u001b]5522;type=write:name=YXBw:pw={password}\u001b\\"));
        processor.Process("\u001b]5522;type=wdata\u001b\\"u8);
        Assert.NotNull(write);
        Assert.False(write.CanRemember);
    }
}
