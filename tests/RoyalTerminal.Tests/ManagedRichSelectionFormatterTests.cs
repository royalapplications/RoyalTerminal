// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedRichSelectionFormatterTests
{
    public static IEnumerable<object[]> Selections()
    {
        foreach (TerminalSnapshotExportFormat format in new[] { TerminalSnapshotExportFormat.StyledVt, TerminalSnapshotExportFormat.Html })
        {
            yield return [format, "a界b", 2, 0, 2, 0, false, false, "界"];
            yield return [format, "a界b", 1, 0, 1, 0, false, false, "界"];
            yield return [format, "abc界Z", 3, 0, 3, 0, false, true, "界"];
            yield return [format, "abc界Z", 3, 0, 3, 0, false, false, ""];
            yield return [format, "abc界Z", 0, 0, 3, 0, false, false, "abc"];
            yield return [format, "abc界Z", 3, 0, 0, 1, false, false, "界"];
            yield return [format, "a界b\r\nc界d", 2, 1, 2, 0, true, true, "界\n界"];
            yield return [format, "ABCD\r\nEFGH", 2, 1, 1, 0, true, true, "BC\nFG"];
            yield return [format, "ABCDE", 0, 0, 0, 1, true, true, "A\nE"];
            yield return [format, "ABCD", int.MinValue, 0, int.MinValue, 0, false, false, "A"];
            yield return [format, "ABCD", int.MaxValue, 0, int.MaxValue, 0, false, false, "D"];
            yield return [format, "ABCD", 3, -2, 0, -1, false, false, "ABCD"];
            yield return [format, "a😀b", 2, 0, 2, 0, false, true, "😀"];
            yield return [format, "e\u0301<&", 0, 0, 2, 0, false, true, "e\u0301<&"];
        }
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public void SelectionBoundariesMatchNative(TerminalSnapshotExportFormat format, string text,
        int x1, int y1, int x2, int y2, bool rectangle, bool unwrap, string expected)
    {
        byte[] input = Encoding.UTF8.GetBytes("\u001b[4:3;38;5;42m" + text);
        TerminalSnapshotExportOptions options = new(Unwrap: unwrap, TrimTrailingWhitespace: true,
            Selection: new(x1, y1, x2, y2, rectangle));
        using BasicVtProcessor managed = new(new(4, 8));
        managed.Process(input);
        Assert.True(managed.TryExportSnapshot(format, options, out string actual));
        Assert.Equal(expected, Text(format, actual));
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyVtProcessor native = new(new(4, 8));
        native.Process(input);
        native.TryExportSnapshot(format, options, out string oracle);
        Assert.Equal(Text(format, oracle), Text(format, actual));
    }

    [Theory]
    [InlineData(TerminalSnapshotExportFormat.StyledVt, false)]
    [InlineData(TerminalSnapshotExportFormat.StyledVt, true)]
    [InlineData(TerminalSnapshotExportFormat.Html, false)]
    [InlineData(TerminalSnapshotExportFormat.Html, true)]
    public void OutOfRangeRowsClampToScrolledViewport(TerminalSnapshotExportFormat format, bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(8, 3, 100);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("zero\r\none\r\ntwo\r\nthree\r\nfour"u8);
        if (processor is ITerminalViewportScrollSource scroll) scroll.SetViewportOffsetRows(1);
        else screen.ScrollOffset = 1;
        ITerminalSnapshotExportSource exporter = (ITerminalSnapshotExportSource)processor;
        Assert.True(exporter.TryExportSnapshot(format, new(TrimTrailingWhitespace: true,
            Selection: new(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue)), out string snapshot));
        Assert.Equal("one\ntwo\nthree", Text(format, snapshot));
    }

    [Theory]
    [InlineData(TerminalSnapshotExportFormat.StyledVt, false)]
    [InlineData(TerminalSnapshotExportFormat.StyledVt, true)]
    [InlineData(TerminalSnapshotExportFormat.Html, false)]
    [InlineData(TerminalSnapshotExportFormat.Html, true)]
    public void ExportDoesNotDetachAnyCopyOnWriteRows(TerminalSnapshotExportFormat format, bool selection)
    {
        TerminalScreen screen = new(8, 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process(Encoding.UTF8.GetBytes("a界b\r\nc"));
        TerminalScreen copy = screen.CreateStateCopy();
        Assert.True(processor.TryExportSnapshot(format, new(TrimTrailingWhitespace: true,
            Selection: selection ? new(2, 0, 7, 2) : null), out _));
        for (int row = 0; row < screen.TotalRows; row++)
            Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(screen.GetRow(row).ReadOnlyCells),
                ref MemoryMarshal.GetReference(copy.GetRow(row).ReadOnlyCells)));
    }

    [Theory]
    [InlineData(TerminalSnapshotExportFormat.StyledVt)]
    [InlineData(TerminalSnapshotExportFormat.Html)]
    public void ExpandedWideGlyphPreservesStyleAndHyperlink(TerminalSnapshotExportFormat format)
    {
        using BasicVtProcessor processor = new(new(8, 3));
        processor.Process(Encoding.UTF8.GetBytes("a\u001b]8;;https://example.com/?a=1&b=2\u001b\\\u001b[4:3;38;5;42m界"));
        Assert.True(processor.TryExportSnapshot(format, new(TrimTrailingWhitespace: true,
            Selection: new(2, 0, 2, 0), Extras: new(IncludeHyperlinks: true)), out string snapshot));
        Assert.Equal("界", Text(format, snapshot));
        Assert.Contains(format == TerminalSnapshotExportFormat.Html ? "https://example.com/?a=1&amp;b=2" : "https://example.com/?a=1&b=2", snapshot);
        if (format == TerminalSnapshotExportFormat.Html)
        {
            Assert.Contains("wavy", snapshot);
            return;
        }
        TerminalScreen replayScreen = new(8, 3);
        using BasicVtProcessor replay = new(replayScreen);
        replay.Process(Encoding.UTF8.GetBytes(snapshot));
        TerminalCell cell = replayScreen.GetRow(0).ReadOnlyCells[0];
        Assert.Equal(TerminalUnderlineStyle.Curly, cell.UnderlineStyle);
        Assert.Equal(TerminalColorIdentity.Palette(42), cell.ForegroundIdentity);
        Assert.NotEqual(0, cell.HyperlinkId);
    }

    private static string Text(TerminalSnapshotExportFormat format, string snapshot)
    {
        if (format == TerminalSnapshotExportFormat.StyledVt)
        {
            using BasicVtProcessor replay = new(new(80, 20));
            replay.Process(Encoding.UTF8.GetBytes(snapshot));
            Assert.True(replay.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText,
                new(TrimTrailingWhitespace: true), out string text));
            return text;
        }
        StringBuilder result = new();
        bool tag = false;
        foreach (char c in snapshot)
        {
            if (c == '<') tag = true;
            else if (c == '>') tag = false;
            else if (!tag) result.Append(c);
        }
        return WebUtility.HtmlDecode(result.ToString());
    }
}
