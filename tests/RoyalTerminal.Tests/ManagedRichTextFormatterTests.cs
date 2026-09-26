// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net;
using System.Text;
using System.Xml.Linq;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedRichTextFormatterTests
{
    // Ghostty PageFormatter defers textless rows (even with background colors)
    // and erased cells, retains explicit spaces in rich exports, and resets
    // style at hard breaks. Windows Terminal/xterm.js also serialize style runs;
    // their clipboard wrappers differ, so HTML bytes are not the contract.
    public static IEnumerable<object[]> BlankCases()
    {
        foreach (bool unwrap in new[] { false, true })
        foreach (bool trim in new[] { false, true })
        {
            yield return ["", unwrap, trim];
            yield return ["A", unwrap, trim];
            yield return ["A  ", unwrap, trim];
            yield return ["\u001b[44m\u001b[2J", unwrap, trim];
            yield return ["\u001b[44mA\u001b[0m\r\n\r\n  B", unwrap, trim];
            yield return ["A\u001b[44m\u001b[K\u001b[0m\r\nB", unwrap, trim];
            yield return ["\r\n\r\nA\r\n\r\n", unwrap, trim];
            yield return ["1234    B", unwrap, trim];
            yield return ["A\u001b[3CB", unwrap, trim];
            yield return ["A\u00a0\u2003", unwrap, trim];
        }
    }

    [Theory]
    [MemberData(nameof(BlankCases))]
    public void DeferredBlanksMatchNative(string text, bool unwrap, bool trim)
    {
        byte[] input = Encoding.UTF8.GetBytes(text);
        TerminalSnapshotExportOptions options = new(Unwrap: unwrap, TrimTrailingWhitespace: trim);
        using BasicVtProcessor managed = new(new(8, 8));
        managed.Process(input);
        Assert.True(managed.TryExportSnapshot(TerminalSnapshotExportFormat.Html, options, out string html));
        Assert.True(managed.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, options, out string vt));
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyVtProcessor native = new(new(8, 8));
        native.Process(input);
        native.TryExportSnapshot(TerminalSnapshotExportFormat.Html, options, out string nativeHtml);
        native.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, options, out string nativeVt);
        Assert.Equal(HtmlText(nativeHtml), HtmlText(html));
        Assert.Equal(ReplayText(nativeVt), ReplayText(vt));
    }

    [Fact]
    public void BackgroundDoesNotBleedAcrossHardLineBreak()
    {
        using BasicVtProcessor processor = new(new(8, 4));
        processor.Process("\u001b[44mA\u001b[0m\r\n\r\n  B"u8);
        Assert.True(processor.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(Extras: new(IncludeStyle: true)), out string snapshot));
        Assert.Contains("\u001b[0m\r\n\r\n", snapshot);
        foreach (bool native in new[] { false, true })
        {
            if (native && !GhosttyVtProcessor.IsAvailable()) continue;
            TerminalScreen screen = new(8, 4);
            using IVtProcessor replay = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
            replay.Process(Encoding.UTF8.GetBytes(snapshot));
            Assert.Equal(TerminalColorIdentity.Palette(4), screen.GetRow(0).ReadOnlyCells[0].BackgroundIdentity);
            Assert.Equal(default, screen.GetRow(2).ReadOnlyCells[0].BackgroundIdentity);
            Assert.Equal((int)'B', screen.GetRow(2).ReadOnlyCells[2].Codepoint);
        }
    }

    [Fact]
    public void HtmlUsesBalancedStyleAndLinkRunsWithTerminalPresentation()
    {
        using BasicVtProcessor processor = new(new(80, 4));
        processor.Process("\u001b]8;;https://example.com/?a=1&b=2\u001b\\\u001b[1mABC\u001b[2;5mDEF\u001b]8;;\u001b\\G\r\nH"u8);
        Assert.True(processor.TryExportSnapshot(TerminalSnapshotExportFormat.Html,
            new(Extras: new(IncludeHyperlinks: true)), out string html));
        XDocument document = XDocument.Parse(html, LoadOptions.PreserveWhitespace);
        Assert.Equal("ABCDEFG\nH", document.Root!.Value);
        Assert.Equal(4, document.Descendants("span").Count());
        Assert.Equal(2, document.Descendants("a").Count());
        Assert.All(document.Descendants("a"), link =>
            Assert.Equal("https://example.com/?a=1&b=2", (string?)link.Attribute("href")));
        Assert.Contains("font-family:monospace", html);
        Assert.Contains("opacity:0.5", html);
        Assert.Contains("text-decoration-line: blink", html);
        Assert.Contains("background-color:", (string?)document.Descendants("pre").Single().Attribute("style"));
    }

    [Fact]
    public void HtmlEscapesTextAndLinkAttributesWithoutSplittingGraphemes()
    {
        using BasicVtProcessor processor = new(new(80, 4));
        processor.Process(Encoding.UTF8.GetBytes("\u001b]8;;https://example.com/\"'&<>\u001b\\e\u0301😀<&>\"'"));
        Assert.True(processor.TryExportSnapshot(TerminalSnapshotExportFormat.Html,
            new(Extras: new(IncludeHyperlinks: true)), out string html));
        XDocument document = XDocument.Parse(html, LoadOptions.PreserveWhitespace);
        Assert.Equal("e\u0301😀<&>\"'", document.Root!.Value);
        Assert.Equal("https://example.com/\"'&<>", (string?)document.Descendants("a").Single().Attribute("href"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectionReplayPreservesCellsAndActivePen(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        using BasicVtProcessor processor = new(new(8, 4));
        processor.Process("\u001b[1\"qA\u001b[0\"qB\u001b[1\"q"u8);
        Assert.True(processor.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(Extras: new(IncludeProtection: true)), out string snapshot));
        TerminalScreen screen = new(8, 4);
        using IVtProcessor replay = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        replay.Process(Encoding.UTF8.GetBytes(snapshot));
        replay.Process("C"u8);
        Assert.True(screen.GetRow(0).ReadOnlyCells[0].IsProtected);
        Assert.False(screen.GetRow(0).ReadOnlyCells[1].IsProtected);
        Assert.True(screen.GetRow(0).ReadOnlyCells[2].IsProtected);
        replay.Process("\u001b[?2J"u8);
        Assert.Equal((int)'A', screen.GetRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal(0, screen.GetRow(0).ReadOnlyCells[1].Codepoint);
    }

    [Theory]
    [InlineData("\u001bV", "\u001bW")]
    [InlineData("\u001b[1\"q", "\u001b[0\"q")]
    public void ProtectionFamilySurvivesInactivePen(string protect, string unprotect)
    {
        using BasicVtProcessor processor = new(new(8, 4));
        processor.Process(Encoding.UTF8.GetBytes(protect + "A" + unprotect));
        Assert.True(processor.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(Extras: new(IncludeProtection: true)), out string snapshot));
        TerminalScreen screen = new(8, 4);
        using BasicVtProcessor replay = new(screen);
        replay.Process(Encoding.UTF8.GetBytes(snapshot));
        replay.Process("B\u001b[2J"u8);
        Assert.Equal(protect == "\u001bV" ? (int)'A' : 0, screen.GetRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal(0, screen.GetRow(0).ReadOnlyCells[1].Codepoint);
    }

    [Fact]
    public void ProtectionIsOptInAndPendingWrapRestoresProtectedGlyph()
    {
        using BasicVtProcessor processor = new(new(2, 4));
        processor.Process("\u001b[1\"qAB\u001b[0\"q"u8);
        processor.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, new(), out string ordinary);
        Assert.DoesNotContain("\"q", ordinary);
        Assert.True(processor.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(Extras: new(IncludeCursor: true, IncludeProtection: true)), out string snapshot));
        TerminalScreen screen = new(2, 4);
        using BasicVtProcessor replay = new(screen);
        replay.Process(Encoding.UTF8.GetBytes(snapshot));
        replay.Process("C"u8);
        Assert.True(screen.GetRow(0).ReadOnlyCells[1].IsProtected);
        Assert.False(screen.GetRow(1).ReadOnlyCells[0].IsProtected);
        Assert.Equal((int)'C', screen.GetRow(1).ReadOnlyCells[0].Codepoint);
    }

    private static string ReplayText(string snapshot)
    {
        using BasicVtProcessor replay = new(new(80, 20));
        replay.Process(Encoding.UTF8.GetBytes(snapshot));
        replay.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, new(TrimTrailingWhitespace: true), out string text);
        return text;
    }

    private static string HtmlText(string html)
    {
        StringBuilder result = new();
        bool tag = false;
        foreach (char c in html)
        {
            if (c == '<') tag = true;
            else if (c == '>') tag = false;
            else if (!tag) result.Append(c);
        }
        return WebUtility.HtmlDecode(result.ToString());
    }
}
