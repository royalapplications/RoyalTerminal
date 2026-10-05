// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class FontCacheKeyBenchmark
{
    // Paired pre-c504031d key layouts, with no font discovery, Skia or drawing.
    // This isolates dictionary identity/storage work, not whole-renderer speed.
    internal static void Run()
    {
        const int count = 512;
        LegacyCodepointKey[] legacyCodepoints = new LegacyCodepointKey[count];
        CollectionCodepointKey[] codepoints = new CollectionCodepointKey[count];
        LegacyRasterKey[] legacyRasters = new LegacyRasterKey[count];
        RasterFontCacheKey[] rasters = new RasterFontCacheKey[count];
        string[] cultures = ["", "pl-PL", "en-US", "ja-JP"];
        for (int index = 0; index < count; index++)
        {
            TerminalTypefaceStyle style = (TerminalTypefaceStyle)(index % 4);
            bool? presentation = (index % 3) switch { 0 => null, 1 => false, _ => true };
            string culture = cultures[index % cultures.Length];
            legacyCodepoints[index] = new(style, 0x100 + index, presentation, culture);
            codepoints[index] = new(style, 0x100 + index, presentation, culture);
            TerminalFontRenderingSettings settings = new()
            {
                SubpixelPositioning = (index & 1) != 0, BaselineSnap = (index & 2) != 0,
                EmbeddedBitmaps = (index & 4) != 0, Embolden = (index & 8) != 0,
                ForceAutoHinting = (index & 16) != 0, LinearMetrics = (index & 32) != 0,
                Edging = (TerminalFontEdging)(index % 3), Hinting = (TerminalFontHinting)(index % 4),
            };
            nint handle = index / 8 + 1;
            int sizeBits = BitConverter.SingleToInt32Bits(14 + (index % 8) * 0.25f);
            TerminalFontSynthesis synthesis = (TerminalFontSynthesis)(index % 4);
            legacyRasters[index] = new(handle, sizeBits, settings.SubpixelPositioning, settings.Edging,
                settings.Hinting, settings.BaselineSnap, settings.EmbeddedBitmaps, settings.Embolden,
                settings.ForceAutoHinting, settings.LinearMetrics, synthesis);
            rasters[index] = new(handle, sizeBits, settings, synthesis);
        }
        RunKeys("codepoint/previous", legacyCodepoints);
        RunKeys("codepoint/packed", codepoints);
        RunKeys("raster/previous", legacyRasters);
        RunKeys("raster/packed", rasters);

        TerminalFontRenderingSettings configured = new();
        // A conservative baseline isolates the old record copy for already-valid
        // settings; it excludes the old enum-validation overhead.
        Measure("normalization/copy-baseline", () =>
        {
            TerminalFontRenderingSettings copy = configured with { };
            GC.KeepAlive(copy);
            return copy.SubpixelPositioning ? 1 : 0;
        }, 200_000);
        Measure("normalization/reuse", () =>
        {
            TerminalFontRenderingSettings normalized = configured.Normalize();
            GC.KeepAlive(normalized);
            return normalized.SubpixelPositioning ? 1 : 0;
        }, 200_000);
    }

    private static void RunKeys<T>(string name, T[] keys) where T : struct
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        Dictionary<T, int> cache = new(keys.Length);
        for (int index = 0; index < keys.Length; index++) cache.Add(keys[index], index);
        long storage = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"{name}: key={Unsafe.SizeOf<T>()} bytes; dictionary construction={storage} allocated bytes");
        Measure(name, () =>
        {
            int checksum = 0;
            foreach (T key in keys) checksum += cache[key];
            return checksum;
        }, 2000);
    }

    private static void Measure(string name, Func<int> work, int iterations)
    {
        for (int index = 0; index < 1000; index++) work();
        double[] times = new double[7];
        long[] allocations = new long[7];
        long checksum = 0;
        for (int sample = 0; sample < times.Length; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int index = 0; index < iterations; index++) checksum += work();
            times[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(times); Array.Sort(allocations);
        Console.WriteLine(FormattableString.Invariant($"{name}: median={times[3]:F3} ms; allocated={allocations[3]} bytes; checksum={checksum}"));
    }

    private readonly record struct LegacyCodepointKey(TerminalTypefaceStyle Style,
        int Codepoint, bool? Presentation, string CultureName);

    private readonly record struct LegacyRasterKey(nint TypefaceHandle, int FontSizeBits,
        bool SubpixelPositioning, TerminalFontEdging Edging, TerminalFontHinting Hinting,
        bool BaselineSnap, bool EmbeddedBitmaps, bool Embolden, bool ForceAutoHinting,
        bool LinearMetrics, TerminalFontSynthesis Synthesis);
}
