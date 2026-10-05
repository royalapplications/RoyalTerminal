// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty d1cd56a56 DynamicPalette shares the built-in original palette on
// snapshot decode, owns custom originals, and preserves an independent override
// mask. WT RenderSettings restores default indexed colors; xterm ThemeService
// restores saved ANSI colors. Royal shares only immutable palettes whose RGB
// values AND empty original-override metadata match; protocol overrides remain
// separate even when their values equal defaults. This changes storage, not OSC.
public sealed class ManagedSnapshotPaletteSharingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RestoreSharesMatchingConfiguredHostOrCanonicalPalette(int candidate)
    {
        TerminalTheme source = candidate == 2 ? TerminalTheme.Dark : CustomTheme(0xFF123456);
        TerminalTheme configured = candidate == 0 ? source : CustomTheme(0xFF456789);
        TerminalTheme host = candidate == 1 ? source : CustomTheme(0xFFABCDEF);
        ManagedTerminalColors colors = new(configured);
        colors.InstallSnapshot(State(source), host);
        Assert.Same(source.Palette, colors.GetEffectiveTheme().Palette);
        for (int index = 0; index < 256; index++)
        {
            Assert.Equal(source.Palette[index], colors.GetOriginalPalette(index));
            Assert.False(colors.HasPaletteOverride(index));
        }
    }

    [Fact]
    public void CustomDecodedPaletteIsOwnedOnceAndCanBeReusedOnReinstallation()
    {
        TerminalTheme source = CustomTheme(0xFF123456);
        GhosttySnapshotTerminalState state = State(source);
        ManagedTerminalColors colors = new(TerminalTheme.Dark);
        colors.InstallSnapshot(state, TerminalTheme.Light);
        TerminalPalette restored = colors.GetEffectiveTheme().Palette;
        Assert.NotSame(source.Palette, restored);
        Assert.Equal(source.Palette.ToArray(), restored.ToArray());
        colors.InstallSnapshot(state, TerminalTheme.Light);
        Assert.Same(restored, colors.GetEffectiveTheme().Palette);
        ManagedTerminalColors other = new(TerminalTheme.Dark);
        other.InstallSnapshot(state, TerminalTheme.Light);
        Assert.NotSame(restored, other.GetEffectiveTheme().Palette); // No unbounded interning cache.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void EqualToDefaultProtocolOverrideKeepsItsMaskAndResetsToSharedOriginal(int index)
    {
        TerminalTheme source = CustomTheme(0xFF123456);
        string command = $"\u001b]4;{index};#{source.Palette[index] & 0xFFFFFF:x6}\a";
        ManagedTerminalColors colors = new(source);
        colors.InstallSnapshot(State(source, command), source);
        Assert.True(colors.HasPaletteOverride(index));
        Assert.True(colors.GetEffectiveTheme().Palette.IsExplicitOverride(index));
        Assert.NotSame(source.Palette, colors.GetEffectiveTheme().Palette);
        colors.ResetPalette(index);
        Assert.Same(source.Palette, colors.GetEffectiveTheme().Palette);
        Assert.False(colors.HasPaletteOverride(index));
    }

    [Fact]
    public void DynamicRgbOverrideDoesNotRequireASeparatePalette()
    {
        TerminalTheme source = CustomTheme(0xFF123456);
        ManagedTerminalColors colors = new(source);
        colors.InstallSnapshot(State(source, "\u001b]10;#abcdef\a"), source);
        Assert.Equal(0xFFABCDEFu, colors.GetDynamic(10));
        Assert.Same(source.Palette, colors.GetEffectiveTheme().Palette);
        colors.SetDynamic(10, null);
        Assert.Same(source.Palette, colors.GetEffectiveTheme().Palette);
        Assert.Equal(source.DefaultForeground, colors.GetDynamic(10));
    }

    [Fact]
    public void HostPaletteFlagsCannotLeakIntoSnapshotOriginalMetadata()
    {
        TerminalTheme source = CustomTheme(0xFF123456);
        TerminalPalette flagged = source.Palette.WithColor(7, source.Palette[7]);
        TerminalTheme host = source.WithPalette(flagged);
        ManagedTerminalColors colors = new(host);
        colors.InstallSnapshot(State(source), host);
        TerminalPalette restored = colors.GetEffectiveTheme().Palette;
        Assert.NotSame(flagged, restored);
        Assert.False(restored.IsExplicitOverride(7));
        Assert.Equal(flagged.ToArray(), restored.ToArray());
    }

    [Fact]
    public void SharedOriginalIsUnaffectedByAnotherOwnersChangesAndConfiguration()
    {
        TerminalTheme source = CustomTheme(0xFF123456);
        GhosttySnapshotTerminalState state = State(source);
        ManagedTerminalColors first = new(source), second = new(source);
        first.InstallSnapshot(state, source);
        second.InstallSnapshot(state, source);
        TerminalPalette retained = first.GetEffectiveTheme().Palette;
        first.SetPalette(7, 0xFF987654);
        Assert.Equal(source.Palette[7], second.GetPalette(7));
        Assert.Same(retained, second.GetEffectiveTheme().Palette);
        first.Configure(CustomTheme(0xFFABCDEF));
        Assert.Equal(0xFF987654u, first.GetPalette(7));
        first.ResetPalette(null);
        Assert.Equal(0xFFABCDEFu, first.GetPalette(7));
        Assert.Equal(0xFF123456u, retained[7]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(767)]
    [InlineData(769)]
    public void SnapshotRgbHelpersRequireExactly256Triples(int length)
    {
        byte[] bytes = new byte[length];
        Assert.False(TerminalTheme.Dark.Palette.MatchesSnapshotRgb(bytes));
        Assert.Throws<ArgumentException>(() => TerminalPalette.FromSnapshotRgb(bytes));
    }

    [Fact]
    public void DecodingOwnsItsBytesAndNormalizesAlphaWithoutSharingFlaggedCandidates()
    {
        byte[] rgb = State(CustomTheme(0xFF123456)).OriginalPaletteRgb.ToArray();
        TerminalPalette palette = TerminalPalette.FromSnapshotRgb(rgb);
        uint expected = palette[7];
        Array.Clear(rgb);
        Assert.Equal(expected, palette[7]);
        for (int index = 0; index < 256; index++)
        {
            Assert.Equal(0xFFu, palette[index] >> 24);
            Assert.False(palette.IsExplicitOverride(index));
        }
        rgb = State(CustomTheme(0xFF123456)).OriginalPaletteRgb.ToArray();
        uint[] transparent = palette.ToArray();
        transparent[0] &= 0xFFFFFF;
        Assert.False(new TerminalPalette(transparent).MatchesSnapshotRgb(rgb));
        Assert.False(palette.WithColor(7, expected).MatchesSnapshotRgb(rgb));
    }

    [Fact]
    public void WarmMatchingIsAllocationFreeAndRestoreDoesNotAllocatePaletteArrays()
    {
        TerminalTheme source = CustomTheme(0xFF123456);
        GhosttySnapshotTerminalState state = State(source);
        ManagedTerminalColors colors = new(source);
        for (int index = 0; index < 100; index++) colors.InstallSnapshot(state, source);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool matched = true;
        for (int index = 0; index < 1000; index++) matched &= source.Palette.MatchesSnapshotRgb(state.OriginalPaletteRgb);
        long matchAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++) colors.InstallSnapshot(state, source);
        long restoreAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(matched);
        Assert.Equal(0, matchAllocated);
        // Theme objects are expected; even one 256-entry array per restore
        // exceeds this bound. No assertion of whole-restore zero allocation.
        Assert.InRange(restoreAllocated, 1, 1000 * 1024 - 1);
        Assert.Same(source.Palette, colors.GetEffectiveTheme().Palette);
    }

    private static TerminalTheme CustomTheme(uint color)
    {
        uint[] colors = TerminalTheme.Dark.Palette.ToArray();
        colors[7] = color;
        return TerminalTheme.Dark.WithPalette(new TerminalPalette(colors));
    }
    private static GhosttySnapshotTerminalState State(TerminalTheme theme, string input = "")
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2));
        processor.ApplyTheme(theme);
        if (input.Length != 0) processor.Process(Encoding.ASCII.GetBytes(input));
        using GhosttySnapshotStateReader reader = new(processor.GetBinarySnapshot(), new());
        return reader.ReadReady().Terminal;
    }
}
