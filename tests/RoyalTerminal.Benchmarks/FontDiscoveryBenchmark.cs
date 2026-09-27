// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Globalization;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;

internal static class FontDiscoveryBenchmark
{
    internal static void Run(bool warmRegistry)
    {
        if (warmRegistry)
        {
            long warmupStarted = Stopwatch.GetTimestamp();
            bool warmed = TerminalFontWarmup.StartAsync().GetAwaiter().GetResult();
            Console.WriteLine(FormattableString.Invariant($"Background font warmup completed: {warmed}; {Stopwatch.GetElapsedTime(warmupStarted).TotalMilliseconds:F3} ms. Its time is reported separately, not eliminated."));
        }
        long started = Stopwatch.GetTimestamp();
        using SKTypeface primary = SKTypeface.FromFamilyName("monospace");
        using (TerminalFontResolver cold = new())
        {
            TerminalFontResolution first = cold.ResolveTypeface(primary, 0x1F600, CultureInfo.InvariantCulture);
            Console.WriteLine(FormattableString.Invariant($"First process font query + resolver + emoji: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms; {first.Typeface.FamilyName}"));
        }

        // A fresh resolver per pass measures discovery rather than the existing
        // per-codepoint cache. Font database/JIT startup is excluded below.
        const int resolvers = 20;
        const int emojiCount = 80;
        TerminalTypefaceCollection collection = new(new TerminalTypefaceEntry(primary, TerminalTypefaceStyle.Regular));
        foreach (bool useCollection in new[] { false, true })
        {
            for (int i = 0; i < 10; i++) ResolveNew(useCollection);
            double[] milliseconds = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < milliseconds.Length; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                started = Stopwatch.GetTimestamp();
                for (int i = 0; i < resolvers; i++) ResolveNew(useCollection);
                milliseconds[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(milliseconds);
            Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"{(useCollection ? "Loaded collection" : "Single-face baseline")}: {resolvers} fresh resolvers x {emojiCount} emoji, median of seven warmed samples: {milliseconds[3]:F3} ms; {allocations[3]} allocated bytes"));
        }

        void ResolveNew(bool useCollection)
        {
            using TerminalFontResolver resolver = new();
            for (int i = 0; i < emojiCount; i++)
                _ = useCollection
                    ? resolver.ResolveTypeface(collection, TerminalTypefaceStyle.Regular, 0x1F600 + i, CultureInfo.InvariantCulture)
                    : resolver.ResolveTypeface(primary, 0x1F600 + i, CultureInfo.InvariantCulture);
        }
    }
}
