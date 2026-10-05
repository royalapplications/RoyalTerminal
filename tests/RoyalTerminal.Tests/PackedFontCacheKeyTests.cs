// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class PackedFontCacheKeyTests
{
    [Fact]
    public void CodepointIdentityKeepsStylesSelectorsCulturesAndAllCodepointBits()
    {
        HashSet<CollectionCodepointKey> keys = [];
        foreach (int codepoint in new[] { 0, 32, 0xFFFF, 0x10000, 0x10FFFF, -1, int.MinValue })
        for (int style = 0; style < 4; style++)
        foreach (bool? presentation in new bool?[] { null, false, true })
        foreach (string culture in new[] { "", "pl-PL", "en-US", "en-us" })
        {
            CollectionCodepointKey key = new((TerminalTypefaceStyle)style, codepoint, presentation, culture);
            Assert.True(keys.Add(key));
            // String identity remains ordinal value equality, not object identity.
            CollectionCodepointKey copy = new((TerminalTypefaceStyle)style, codepoint, presentation, new string(culture.ToCharArray()));
            Assert.Equal(key, copy);
            Assert.Equal(key.GetHashCode(), copy.GetHashCode());
        }
        Assert.Equal(7 * 4 * 3 * 4, keys.Count);
    }

    [Fact]
    public void EveryRasterOptionCombinationHasIndependentIdentity()
    {
        HashSet<RasterFontCacheKey> keys = [];
        for (int flags = 0; flags < 64; flags++)
        for (int edging = 0; edging < 3; edging++)
        for (int hinting = 0; hinting < 4; hinting++)
        for (int synthesis = 0; synthesis < 4; synthesis++)
        {
            TerminalFontRenderingSettings settings = new()
            {
                SubpixelPositioning = (flags & 1) != 0,
                BaselineSnap = (flags & 2) != 0,
                EmbeddedBitmaps = (flags & 4) != 0,
                Embolden = (flags & 8) != 0,
                ForceAutoHinting = (flags & 16) != 0,
                LinearMetrics = (flags & 32) != 0,
                Edging = (TerminalFontEdging)edging,
                Hinting = (TerminalFontHinting)hinting,
            };
            RasterFontCacheKey key = new(123, 0x41400000, settings, (TerminalFontSynthesis)synthesis);
            Assert.True(keys.Add(key));
            Assert.Equal(key, new RasterFontCacheKey(123, 0x41400000, settings with { }, (TerminalFontSynthesis)synthesis));
        }
        Assert.Equal(64 * 3 * 4 * 4, keys.Count);
    }

    [Fact]
    public void RasterIdentityKeepsSignedZeroNanPayloadAndFullNativeHandle()
    {
        HashSet<RasterFontCacheKey> keys = [];
        nint[] handles = IntPtr.Size == 8 ? [0, 1, unchecked((nint)(1L << 32)), unchecked((nint)(1L << 48)), -1] : [0, 1, -1];
        foreach (nint handle in handles)
        foreach (int size in new[] { 0, int.MinValue, 0x7FC00000, 0x7FC00001, 0x41400000, 0x41400001 })
            Assert.True(keys.Add(new(handle, size, TerminalFontRenderingSettings.Default, TerminalFontSynthesis.None)));
        Assert.Equal(handles.Length * 6, keys.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void InvalidPackedEnumsCannotAliasValidEntries(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionCodepointKey((TerminalTypefaceStyle)value, 65, null, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterFontCacheKey(1, 0,
            TerminalFontRenderingSettings.Default, (TerminalFontSynthesis)value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterFontCacheKey(1, 0,
            TerminalFontRenderingSettings.Default with { Edging = (TerminalFontEdging)value }, TerminalFontSynthesis.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RasterFontCacheKey(1, 0,
            TerminalFontRenderingSettings.Default with { Hinting = (TerminalFontHinting)value }, TerminalFontSynthesis.None));
    }

    [Fact]
    public void PackedKeysKeepSmallFixedLayouts()
    {
        Assert.InRange(Unsafe.SizeOf<CollectionCodepointKey>(), IntPtr.Size + 8, 16);
        Assert.InRange(Unsafe.SizeOf<RasterFontCacheKey>(), IntPtr.Size + 8, 16);
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(3, 0)]
    [InlineData(0, 4)]
    [InlineData(int.MaxValue, int.MinValue)]
    [InlineData(2, 3)]
    [InlineData(0, 0)]
    public void NormalizationRepairsOnlyInvalidEnumsAndRetainsOtherOptions(int edging, int hinting)
    {
        TerminalFontRenderingSettings settings = new()
        {
            Edging = (TerminalFontEdging)edging, Hinting = (TerminalFontHinting)hinting,
            Embolden = true, ForceAutoHinting = true, LinearMetrics = true,
            BaselineSnap = false, SubpixelPositioning = false, EmbeddedBitmaps = false,
        };
        TerminalFontRenderingSettings normalized = settings.Normalize();
        Assert.Equal(settings with
        {
            Edging = (uint)edging <= 2 ? settings.Edging : TerminalFontRenderingSettings.Default.Edging,
            Hinting = (uint)hinting <= 3 ? settings.Hinting : TerminalFontRenderingSettings.Default.Hinting,
        }, normalized);
        Assert.Same(normalized, normalized.Normalize());
        if ((uint)edging <= 2 && (uint)hinting <= 3) Assert.Same(settings, normalized);
        else Assert.NotSame(settings, normalized);
    }

    [Fact]
    public void WarmNormalizationAndTypedCacheLookupsAllocateNothing()
    {
        TerminalFontRenderingSettings settings = new() { Embolden = true };
        CollectionCodepointKey codepoint = new(TerminalTypefaceStyle.Bold, 0x1F600, true, "pl-PL");
        RasterFontCacheKey raster = new(123, 0x41400000, settings, TerminalFontSynthesis.Bold);
        Dictionary<CollectionCodepointKey, int> codepoints = new() { [codepoint] = 1 };
        Dictionary<RasterFontCacheKey, int> rasters = new() { [raster] = 1 };
        int Lookup() => codepoints[new(TerminalTypefaceStyle.Bold, 0x1F600, true, "pl-PL")] +
            rasters[new(123, 0x41400000, settings.Normalize(), TerminalFontSynthesis.Bold)];
        for (int index = 0; index < 1000; index++) Lookup();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int index = 0; index < 1000; index++) sum += Lookup();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(2000, sum);
        Assert.Equal(0, allocated);
    }
}
