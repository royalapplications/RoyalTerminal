// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Globalization;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;

internal static class FontLookupBenchmark
{
    internal static void Run()
    {
        using SKTypeface face = SKTypeface.FromFamilyName("monospace");
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular));
        using TerminalFontResolver resolver = new();
        CultureInfo direct = CultureInfo.InvariantCulture;
        CultureInfo general = CultureInfo.GetCultureInfo("pl-PL");
        // The first culture uses the compact table. A different culture exercises
        // the retained general dictionary with the same face/ASCII/style inputs.
        // Warm both paths and font/JIT startup before measuring either one.
        for (int i = 0; i < 100; i++) { Resolve(direct); Resolve(general); }
        foreach (CultureInfo culture in new[] { direct, general })
        {
            double[] elapsed = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < elapsed.Length; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int iteration = 0; iteration < 5000; iteration++) Resolve(culture);
                elapsed[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(elapsed);
            Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"{(ReferenceEquals(culture, direct) ? "Compact ASCII" : "General dictionary")}: 1,900,000 warm lookups; median of seven: {elapsed[3]:F3} ms; {allocations[3]} allocated bytes"));
        }

        void Resolve(CultureInfo culture)
        {
            for (TerminalTypefaceStyle style = TerminalTypefaceStyle.Regular; style <= TerminalTypefaceStyle.BoldItalic; style++)
                for (int cp = 0x20; cp <= 0x7E; cp++)
                    _ = resolver.ResolveTypeface(collection, style, cp, culture);
        }
    }
}
