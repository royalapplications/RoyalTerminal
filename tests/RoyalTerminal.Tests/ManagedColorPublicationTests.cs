// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty DynamicPalette (d1cd56a56) shares configured defaults but preserves
// explicit equal-to-default overrides across configuration changes. WT's
// RenderSettings and xterm.js ThemeService likewise keep reset/default state
// separate from render colors. Only immutable render views are cached here;
// protocol override identity must never be inferred from visual equality.
public sealed class ManagedColorPublicationTests
{
    [Fact]
    public void UnchangedStateSharesConfiguredThemeAcrossOwners()
    {
        TerminalTheme configured = TerminalTheme.Dark.WithPaletteColor(7, 0xFF112233);
        ManagedTerminalColors first = new(configured), second = new(configured);

        Assert.Same(configured, first.GetEffectiveTheme());
        Assert.Same(configured, second.GetEffectiveTheme());
        first.SetPalette(7, 0xFF445566);
        Assert.Equal(0xFF112233u, second.GetPalette(7));
        Assert.Same(configured, second.GetEffectiveTheme());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void RepeatedOverridesReusePublishedThemeWithoutAllocations(int selector)
    {
        ManagedTerminalColors colors = new(TerminalTheme.Dark);
        colors.SetPalette(42, 0xFF112233);
        colors.SetDynamic(selector, 0xFF445566);
        TerminalTheme published = colors.GetEffectiveTheme();
        for (int i = 0; i < 128; i++) Repeat(colors, selector);

        long before = GC.GetAllocatedBytesForCurrentThread();
        TerminalTheme current = published;
        for (int i = 0; i < 128; i++) current = Repeat(colors, selector);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Same(published, current);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void DynamicChangesReusePaletteButKeepEarlierThemesImmutable(int selector)
    {
        ManagedTerminalColors colors = new(TerminalTheme.Dark);
        colors.SetPalette(255, 0xFF112233);
        colors.SetDynamic(selector, 0xFF445566);
        TerminalTheme first = colors.GetEffectiveTheme();

        colors.SetDynamic(selector, 0xFF778899);
        TerminalTheme second = colors.GetEffectiveTheme();

        Assert.NotSame(first, second);
        Assert.Same(first.Palette, second.Palette);
        Assert.Equal(0xFF445566u, Dynamic(first, selector));
        Assert.Equal(0xFF778899u, Dynamic(second, selector));
        colors.SetDynamic(selector, null);
        TerminalTheme reset = colors.GetEffectiveTheme();
        Assert.Same(first.Palette, reset.Palette);
        Assert.Equal(Dynamic(TerminalTheme.Dark, selector), Dynamic(reset, selector));
        Assert.Null(colors.GetSnapshotDynamic(selector).Override);
    }

    [Fact]
    public void EqualDefaultOverrideSurvivesThemeChangeAndResetRestoresNewestDefault()
    {
        TerminalTheme original = TerminalTheme.Dark.WithPaletteColor(42, 0xFF112233, explicitOverride: false);
        ManagedTerminalColors colors = new(original);
        colors.SetPalette(42, original.Palette[42]);
        TerminalTheme explicitTheme = colors.GetEffectiveTheme();

        Assert.NotSame(original.Palette, explicitTheme.Palette);
        Assert.True(explicitTheme.Palette.IsExplicitOverride(42));
        Assert.True(colors.HasPaletteOverride(42));
        TerminalTheme next = original.WithPaletteColor(42, 0xFF445566, explicitOverride: false);
        colors.Configure(next);
        Assert.Equal(0xFF112233u, colors.GetEffectiveTheme().Palette[42]);
        Assert.Equal(0xFF445566u, colors.GetOriginalPalette(42));
        colors.ResetPalette(42);

        Assert.Same(next, colors.GetEffectiveTheme());
        Assert.False(colors.HasPaletteOverride(42));
        Assert.Equal(0xFF112233u, explicitTheme.Palette[42]);
    }

    [Fact]
    public void ConfigurationWithoutPaletteChangeKeepsMaterializedPalette()
    {
        ManagedTerminalColors colors = new(TerminalTheme.Dark);
        colors.SetPalette(255, 0xFF123456);
        TerminalTheme previous = colors.GetEffectiveTheme();
        TerminalTheme configured = TerminalTheme.Dark.WithCursorTextColor(0xFFABCDEF)
            .WithSelectionColors(0xFF112233, 0xFF445566).WithBoldColor(0xFF778899)
            .WithOscColorReportFormat(TerminalOscColorReportFormat.Bit8);

        colors.Configure(configured);
        TerminalTheme current = colors.GetEffectiveTheme();

        Assert.NotSame(previous, current);
        Assert.Same(previous.Palette, current.Palette);
        Assert.Equal(configured.CursorTextColor, current.CursorTextColor);
        Assert.Equal(configured.SelectionForeground, current.SelectionForeground);
        Assert.Equal(configured.SelectionBackground, current.SelectionBackground);
        Assert.Equal(configured.BoldColor, current.BoldColor);
        Assert.Equal(configured.OscColorReportFormat, current.OscColorReportFormat);
        colors.Configure(configured);
        Assert.Same(current, colors.GetEffectiveTheme());
    }

    [Fact]
    public void ResetOnlyInvalidatesRecordedOverridesAndReleasesPublishedPalette()
    {
        ManagedTerminalColors colors = new(TerminalTheme.Dark);
        colors.SetPalette(1, 0xFF123456);
        colors.SetPalette(2, 0xFFABCDEF);
        TerminalTheme first = colors.GetEffectiveTheme();
        colors.ResetPalette(200);
        Assert.Same(first, colors.GetEffectiveTheme());

        colors.ResetPalette(1);
        TerminalTheme partial = colors.GetEffectiveTheme();
        Assert.NotSame(first.Palette, partial.Palette);
        Assert.Equal(TerminalTheme.Dark.Palette[1], partial.Palette[1]);
        Assert.Equal(0xFFABCDEFu, partial.Palette[2]);
        colors.ResetPalette(null);
        Assert.Same(TerminalTheme.Dark, colors.GetEffectiveTheme());
        colors.ResetPalette(null);
        Assert.Same(TerminalTheme.Dark, colors.GetEffectiveTheme());
        Assert.Equal(0xFF123456u, first.Palette[1]);
    }

    [Fact]
    public void PaletteBatchOwnsArraysAndPreservesBothSourcesOfExplicitFlags()
    {
        uint[] input = TerminalTheme.Dark.Palette.ToArray();
        uint original = input[1];
        TerminalPalette configured = new(input, [1]);
        Dictionary<int, uint> overrides = new() { [255] = 0xFFABCDEF };
        TerminalPalette published = configured.WithOverrides(overrides);
        input[1] = 0;
        overrides[255] = 0;
        uint[] exported = published.ToArray();
        exported[1] = exported[255] = 0;

        Assert.Equal(original, configured[1]);
        Assert.Equal(original, published[1]);
        Assert.Equal(0xFFABCDEFu, published[255]);
        Assert.True(published.IsExplicitOverride(1));
        Assert.True(published.IsExplicitOverride(255));
        Assert.False(configured.IsExplicitOverride(255));
        Assert.Same(configured, configured.WithOverrides([]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleColorCopyRetainsFlagsAndNeverMutatesPublishedArrays(bool explicitOverride)
    {
        TerminalPalette original = TerminalTheme.Dark.Palette.WithColor(1, 0xFF112233);
        TerminalPalette changed = original.WithColor(1, 0xFF445566, explicitOverride)
            .WithColor(42, 0xFF778899, explicitOverride);

        Assert.Equal(0xFF112233u, original[1]);
        Assert.Equal(0xFF445566u, changed[1]);
        Assert.True(changed.IsExplicitOverride(1)); // false does not remove an existing explicit flag.
        Assert.Equal(explicitOverride, changed.IsExplicitOverride(42));
        Assert.False(original.IsExplicitOverride(42));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void InvalidBatchCannotModifySourcePalette(int index)
    {
        TerminalPalette original = TerminalTheme.Dark.Palette;
        uint before = original[0];
        Assert.Throws<ArgumentOutOfRangeException>(() => original.WithOverrides(new() { [0] = 0, [index] = 1 }));
        Assert.Equal(before, original[0]);
    }

    [Fact]
    public void SnapshotInstallationInvalidatesCachedViewsWithoutChangingRetainedOnes()
    {
        using BasicVtProcessor source = new(new TerminalScreen(4, 2));
        source.Process("\u001b]4;42;#123456\u001b\\\u001b]10;#abcdef\u001b\\"u8);
        using GhosttySnapshotStateReader reader = new(source.GetBinarySnapshot(), new());
        GhosttySnapshotTerminalState state = reader.ReadReady().Terminal;
        ManagedTerminalColors colors = new(TerminalTheme.Dark);
        colors.SetPalette(42, 0xFF778899);
        colors.SetDynamic(10, 0xFF445566);
        TerminalTheme retained = colors.GetEffectiveTheme();

        colors.InstallSnapshot(state, TerminalTheme.Light);
        TerminalTheme restored = colors.GetEffectiveTheme();

        Assert.NotSame(retained, restored);
        Assert.Equal(0xFF123456u, restored.Palette[42]);
        Assert.Equal(0xFFABCDEFu, restored.DefaultForeground);
        Assert.True(colors.HasPaletteOverride(42));
        Assert.Same(restored, colors.GetEffectiveTheme());
        Assert.Equal(0xFF778899u, retained.Palette[42]);
        Assert.Equal(0xFF445566u, retained.DefaultForeground);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedProtocolColorUpdatesPreserveCowRowsAndSnapshotMask(bool synchronized)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[38;5;42mA\u001b]4;42;#123456\u001b\\"u8);
        TerminalScreen retained = screen.CreateStateCopy();
        object cells = retained.GetViewportRow(0).SearchStorageIdentity;
        TerminalTheme first = screen.Theme;
        if (synchronized) processor.Process("\u001b[?2026h"u8);
        for (int i = 0; i < 8; i++) processor.Process("\u001b]4;42;#123456\u001b\\"u8);
        if (synchronized) processor.Process("\u001b[?2026l"u8);

        Assert.Same(first, screen.Theme);
        Assert.Same(cells, screen.GetViewportRow(0).SearchStorageIdentity);
        Assert.Equal(0xFF123456u, retained.GetViewportRow(0).ReadOnlyCells[0].Foreground);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(processor.GetBinarySnapshot());
        Assert.True(restored.Processor.SnapshotColors.HasPaletteOverride(42));
        Assert.Equal(0xFF123456u, restored.Screen.Theme.Palette[42]);
        processor.Process("\u001b]4;42;#abcdef\u001b\\"u8);
        Assert.Equal(0xFF123456u, retained.Theme.Palette[42]);
        Assert.Equal(0xFF123456u, retained.GetViewportRow(0).ReadOnlyCells[0].Foreground);
        Assert.Equal(0xFFABCDEFu, screen.GetViewportRow(0).ReadOnlyCells[0].Foreground);
    }

    private static TerminalTheme Repeat(ManagedTerminalColors colors, int selector)
    {
        colors.SetPalette(42, 0xFF112233);
        colors.SetDynamic(selector, 0xFF445566);
        colors.ResetPalette(200);
        return colors.GetEffectiveTheme();
    }

    private static uint Dynamic(TerminalTheme theme, int selector) => selector switch
    {
        10 => theme.DefaultForeground,
        11 => theme.DefaultBackground,
        _ => theme.CursorColor,
    };
}
