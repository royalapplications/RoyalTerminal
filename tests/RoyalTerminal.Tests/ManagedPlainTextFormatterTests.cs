// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedPlainTextFormatterTests
{
    [Theory]
    [InlineData("", false, false, "")]
    [InlineData("A\u001b[4GB", false, false, "A  B")]
    [InlineData("\r\nA\r\n\r\nB\r\n", false, false, "\nA\n\nB")]
    [InlineData("A  ", false, false, "A  ")]
    [InlineData("A  ", false, true, "A")]
    [InlineData("A\u00a0\u2002", false, true, "A\u00a0\u2002")]
    [InlineData("a   b", true, true, "a   b")]
    [InlineData("a   b", false, true, "a\nb")]
    [InlineData("abc界Z", true, true, "abc界Z")]
    [InlineData("abc界Z", false, true, "abc\n界Z")]
    [InlineData("e\u0301😀", true, true, "e\u0301😀")]
    public void BlankCellsSpacesWrapsAndUnicodeMatchNative(string text, bool unwrap, bool trim, string expected)
    {
        byte[] input = Encoding.UTF8.GetBytes(text);
        using BasicVtProcessor managed = new(new TerminalScreen(4, 8));
        managed.Process(input);
        TerminalSnapshotExportOptions options = new(Unwrap: unwrap, TrimTrailingWhitespace: trim);
        Assert.True(managed.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, options, out string actual));
        Assert.Equal(expected, actual);
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(4, 8));
        native.Process(input);
        Assert.True(native.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, options, out string oracle));
        Assert.Equal(oracle, actual);
    }

    [Theory]
    [InlineData("a界b", 2, 0, 2, 0, false, false, "界")]
    [InlineData("abc界Z", 3, 0, 3, 0, false, true, "界")]
    [InlineData("abc界Z", 3, 0, 3, 0, false, false, "")]
    [InlineData("ABCD\r\nEFGH", 2, 1, 1, 0, true, true, "BC\nFG")]
    [InlineData("ABCDE", 0, 0, 0, 1, true, true, "A\nE")]
    [InlineData("A\u001b[4GB", -10, -1, 100, 100, false, false, "A  B")]
    public void SelectionNormalizesClampsAndKeepsWholeWideGlyphs(string text, int x1, int y1,
        int x2, int y2, bool rectangle, bool unwrap, string expected)
    {
        TerminalSelectionRange selection = new(x1, y1, x2, y2, rectangle);
        TerminalSnapshotExportOptions options = new(Unwrap: unwrap, Selection: selection);
        byte[] input = Encoding.UTF8.GetBytes(text);
        using BasicVtProcessor managed = new(new TerminalScreen(4, 8));
        managed.Process(input);
        Assert.True(managed.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, options, out string actual));
        Assert.Equal(expected, actual);
        if (!unwrap) Assert.Equal(expected, managed.ReadSelection(selection));
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(4, 8));
        native.Process(input);
        Assert.True(native.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, options, out string oracle));
        Assert.Equal(oracle, actual);
        if (!unwrap) Assert.Equal(native.ReadSelection(selection), managed.ReadSelection(selection));
    }

    [Fact]
    public void SeededHistoryAndSelectionsMatchNativeAcrossTrimAndWrapOptions()
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        Random random = new(13943);
        string[] tokens = ["abc", " ", "界", "e\u0301", "\u00a0", "\r\n", "\u001b[3G", "\u001b[X"];
        for (int sample = 0; sample < 50; sample++)
        {
            int columns = random.Next(4, 20);
            TerminalScreen managedScreen = new(columns, 5, 100);
            TerminalScreen nativeScreen = new(columns, 5, 100);
            using BasicVtProcessor managed = new(managedScreen);
            using GhosttyVtProcessor native = new(nativeScreen);
            StringBuilder input = new();
            for (int i = 0; i < 100; i++) input.Append(tokens[random.Next(tokens.Length)]);
            byte[] bytes = Encoding.UTF8.GetBytes(input.ToString());
            managed.Process(bytes);
            native.Process(bytes);
            for (int flags = 0; flags < 4; flags++)
            {
                Compare(new(Unwrap: (flags & 1) != 0, TrimTrailingWhitespace: (flags & 2) != 0));
                Compare(new(Unwrap: (flags & 1) != 0, TrimTrailingWhitespace: (flags & 2) != 0,
                    Selection: new(1, 0, columns - 2, 3, Rectangle: (sample & 1) != 0)));
            }

            void Compare(TerminalSnapshotExportOptions options)
            {
                Assert.True(native.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, options, out string expected));
                Assert.True(managed.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, options, out string actual));
                Assert.True(expected == actual, $"Sample {sample}; {options}; expected {Escape(expected)}; actual {Escape(actual)}");
            }
        }
    }

    [Fact]
    public void ExportDoesNotDetachCopyOnWriteCellStorage()
    {
        TerminalScreen screen = new(8, 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process("abc"u8);
        TerminalScreen copy = screen.CreateStateCopy();
        Assert.True(processor.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, new(), out _));
        Assert.Equal("abc", processor.ReadSelection(new(0, 0, 7, 0)));
        Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(screen.GetRow(0).ReadOnlyCells),
            ref MemoryMarshal.GetReference(copy.GetRow(0).ReadOnlyCells)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionUsesScrolledViewportAndExportsWorkingStateDuringRenderHold(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(8, 3, 100);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.Process("zero\r\none\r\ntwo\r\nthree\r\nfour"u8);
        screen.ScrollOffset = 2;
        ITerminalSelectionExportSource selection = (ITerminalSelectionExportSource)processor;
        Assert.Equal("zero\none", selection.ReadSelection(new(0, 0, 7, 1)));
        screen.ScrollOffset = 0;
        processor.Process("\u001b[?2026h\u001b[HNEW\u001b[K"u8);
        Assert.Equal("NEW", selection.ReadSelection(new(0, 0, 7, 0)));
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal("NEW", selection.ReadSelection(new(0, 0, 7, 0)));
    }

    [Theory]
    [InlineData(TerminalSnapshotExportFormat.PlainText, "\n")]
    [InlineData(TerminalSnapshotExportFormat.StyledVt, "\r\n")]
    [InlineData(TerminalSnapshotExportFormat.Html, "\n")]
    public void NativeRectangularExportsKeepRowBoundaries(TerminalSnapshotExportFormat format, string newline)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(4, 4));
        native.Process("ABCDE"u8);
        Assert.True(native.TryExportSnapshot(format, new(Unwrap: true, TrimTrailingWhitespace: true,
            Selection: new(0, 0, 0, 1, Rectangle: true)), out string actual));
        Assert.Contains(newline, actual, StringComparison.Ordinal);
    }

    private static string Escape(string value) => value.Replace("\n", "\\n", StringComparison.Ordinal);
}
