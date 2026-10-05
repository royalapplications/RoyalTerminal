// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedStyledSgrFormatterTests
{
    public static IEnumerable<object[]> Styles()
    {
        for (int underline = 0; underline <= 5; underline++)
            foreach (string color in new[] { "", ";38;5;255;48;5;17;58;5;9", ";38;2;17;34;51;48;2;68;85;102;58;2;119;136;153" })
                yield return [ $"1;2;3;5;7;8;9;53;4:{underline}{color}" ];
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void ExportPreservesAllSgrStylesOnReplayIntoBothEngines(string sgr)
    {
        TerminalScreen sourceScreen = new(16, 3);
        using BasicVtProcessor source = new(sourceScreen);
        Write(source, $"\u001b[{sgr}mX\u001b[0mY");
        TerminalSnapshotExportOptions options = new(TrimTrailingWhitespace: true, Selection: new(0, 0, 1, 0));
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, options, out string snapshot));
        for (int engine = 0; engine < 2; engine++)
        {
            if (engine == 1 && !GhosttyVtProcessor.IsAvailable()) continue;
            TerminalScreen targetScreen = new(16, 3);
            using IVtProcessor target = Create(targetScreen, engine == 1);
            Write(target, snapshot);
            for (int column = 0; column < 2; column++) Compare(sourceScreen.GetRow(0).ReadOnlyCells[column], targetScreen.GetRow(0).ReadOnlyCells[column]);
        }

        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyVtProcessor native = new(new(16, 3));
        Write(native, $"\u001b[{sgr}mX\u001b[0mY");
        Assert.True(native.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt, options, out string oracle));
        TerminalScreen oracleScreen = new(16, 3);
        using BasicVtProcessor replay = new(oracleScreen);
        Write(replay, oracle);
        for (int column = 0; column < 2; column++) Compare(sourceScreen.GetRow(0).ReadOnlyCells[column], oracleScreen.GetRow(0).ReadOnlyCells[column]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualResolvedColorsDoNotMergeDistinctDefaultPaletteAndRgbIdentities(bool nativeTarget)
    {
        if (nativeTarget && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen sourceScreen = new(16, 3);
        using BasicVtProcessor source = new(sourceScreen);
        Write(source, "\u001b]10;#112233\u001b\\\u001b]11;#112233\u001b\\\u001b]4;1;#112233;2;#112233\u001b\\" +
            "\u001b[4:3;38;5;1;48;5;1;58;5;1mA\u001b[38;5;2;48;5;2;58;5;2mB" +
            "\u001b[38;2;17;34;51;48;2;17;34;51;58;2;17;34;51mC\u001b[0mD");
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(TrimTrailingWhitespace: true, Selection: new(0, 0, 3, 0), Extras: new(IncludePalette: true)), out string snapshot));
        TerminalScreen targetScreen = new(16, 3);
        using IVtProcessor target = Create(targetScreen, nativeTarget);
        Write(target, snapshot);
        for (int column = 0; column < 4; column++) Compare(sourceScreen.GetRow(0).ReadOnlyCells[column], targetScreen.GetRow(0).ReadOnlyCells[column]);

        // Palette/default references must continue to follow future OSC changes;
        // truecolor remains independent even when its original RGB was identical.
        const string recolor = "\u001b]4;1;#445566;2;#778899\u001b\\\u001b]10;#abcdef\u001b\\\u001b]11;#aabbcc\u001b\\";
        Write(source, recolor); Write(target, recolor);
        for (int column = 0; column < 4; column++)
        {
            TerminalCell expected = sourceScreen.GetRow(0).ReadOnlyCells[column];
            TerminalCell actual = targetScreen.GetRow(0).ReadOnlyCells[column];
            Compare(expected, actual);
            Assert.Equal(expected.Foreground, actual.Foreground);
            Assert.Equal(expected.Background, actual.Background);
            if (expected.HasUnderlineColor) Assert.Equal(expected.UnderlineColor, actual.UnderlineColor);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IncludeStyleRestoresUnderlineColorAndPenAfterPendingWrap(bool nativeTarget, bool pendingWrap)
    {
        if (nativeTarget && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen sourceScreen = new(4, 3);
        using BasicVtProcessor source = new(sourceScreen);
        Write(source, "\u001b[4:3;58;5;42m" + (pendingWrap ? "abcd" : "a") + "\u001b[4:5;58;2;11;22;33m");
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(TrimTrailingWhitespace: true, Extras: new(IncludeCursor: true, IncludeStyle: true)), out string snapshot));
        TerminalScreen targetScreen = new(4, 3);
        using IVtProcessor target = Create(targetScreen, nativeTarget);
        Write(target, snapshot);
        Write(source, "X"); Write(target, "X");
        int row = pendingWrap ? 1 : 0, column = pendingWrap ? 0 : 1;
        Compare(sourceScreen.GetRow(row).ReadOnlyCells[column], targetScreen.GetRow(row).ReadOnlyCells[column]);
        Compare(sourceScreen.GetRow(0).ReadOnlyCells[0], targetScreen.GetRow(0).ReadOnlyCells[0]);
    }

    [Fact]
    public void EmptyTrimmedScreenStillRestoresRequestedCurrentStyle()
    {
        using BasicVtProcessor source = new(new(8, 3));
        Write(source, "\u001b[4:4;58;5;42m");
        Assert.True(source.TryExportSnapshot(TerminalSnapshotExportFormat.StyledVt,
            new(TrimTrailingWhitespace: true, Extras: new(IncludeStyle: true)), out string snapshot));
        TerminalScreen screen = new(8, 3);
        using BasicVtProcessor target = new(screen);
        Write(target, snapshot + "X");
        TerminalCell cell = screen.GetRow(0).ReadOnlyCells[0];
        Assert.Equal(TerminalUnderlineStyle.Dotted, cell.UnderlineStyle);
        Assert.True(cell.HasUnderlineColor);
        Assert.Equal(TerminalColorIdentity.Palette(42), cell.UnderlineIdentity);
    }

    [Fact]
    public void WarmStyleEmissionDoesNotAllocateWithReusableBuilder()
    {
        TerminalCell cell = new() { ForegroundIdentity = TerminalColorIdentity.Palette(255),
            BackgroundIdentity = TerminalColorIdentity.Rgb(0x123456),
            UnderlineIdentity = TerminalColorIdentity.Rgb(0xABCDEF), HasUnderlineColor = true,
            UnderlineStyle = TerminalUnderlineStyle.Curly, Attributes = CellAttributes.Bold };
        StringBuilder builder = new(256);
        for (int i = 0; i < 100; i++) { builder.Clear(); ManagedSgrFormatter.Append(builder, cell); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { builder.Clear(); ManagedSgrFormatter.Append(builder, cell); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Contains("\u001b[4:3m", builder.ToString(), StringComparison.Ordinal);
        Assert.Contains("\u001b[38;5;255m", builder.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StyleNumbersAreIndependentOfCurrentCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            TerminalCell cell = new() { ForegroundIdentity = TerminalColorIdentity.Rgb(0x112233) };
            StringBuilder builder = new();
            ManagedSgrFormatter.Append(builder, cell);
            Assert.Equal("\u001b[0m\u001b[38;2;17;34;51m", builder.ToString());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static IVtProcessor Create(TerminalScreen screen, bool native)
        => native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
    private static void Write(IVtProcessor processor, string text) => processor.Process(Encoding.UTF8.GetBytes(text));
    private static void Compare(TerminalCell expected, TerminalCell actual)
    {
        Assert.Equal(expected.Codepoint, actual.Codepoint);
        Assert.Equal(expected.Attributes, actual.Attributes);
        Assert.Equal(expected.Decorations, actual.Decorations);
        Assert.Equal(expected.UnderlineStyle, actual.UnderlineStyle);
        Assert.Equal(expected.ForegroundIdentity, actual.ForegroundIdentity);
        Assert.Equal(expected.BackgroundIdentity, actual.BackgroundIdentity);
        Assert.Equal(expected.HasUnderlineColor, actual.HasUnderlineColor);
        Assert.Equal(expected.UnderlineIdentity, actual.UnderlineIdentity);
    }
}
