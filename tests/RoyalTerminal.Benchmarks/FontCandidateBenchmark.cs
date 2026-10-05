// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Globalization;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;

internal static class FontCandidateBenchmark
{
    internal static void Run()
    {
        using SKTypeface face = SKTypeface.FromFamilyName("monospace");
        TerminalTypefaceCollection fonts = new(new TerminalTypefaceEntry(face, TerminalTypefaceStyle.Regular));
        string[] queries = ["#\uFE0F", "\U0001F600\uFE0E", "A\uFE0F", "\U0010FFFF"];
        using TerminalFontResolver warm = new();
        Query(warm);
        Fresh();
        Measure("Cold mixed-presentation/unsupported discovery (3 fresh resolvers)", () =>
        {
            for (int i = 0; i < 3; i++) Fresh();
        });
        Measure("Warm mixed-presentation/unsupported queries (4,000 lookups)", () =>
        {
            for (int i = 0; i < 1000; i++) Query(warm);
        });

        void Fresh()
        {
            using TerminalFontResolver resolver = new();
            Query(resolver);
        }
        void Query(TerminalFontResolver resolver)
        {
            foreach (string query in queries)
                _ = resolver.ResolveTypeface(fonts, TerminalTypefaceStyle.Regular, query, CultureInfo.InvariantCulture);
        }
        static void Measure(string label, Action workload)
        {
            double[] milliseconds = new double[7];
            long[] allocations = new long[7];
            workload();
            for (int sample = 0; sample < 7; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                workload();
                milliseconds[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(milliseconds);
            Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"{label}: median of seven {milliseconds[3]:F3} ms; {allocations[3]} allocated bytes"));
        }
    }
}
