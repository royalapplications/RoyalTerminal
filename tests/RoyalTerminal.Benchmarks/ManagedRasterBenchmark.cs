// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;

internal static class ManagedRasterBenchmark
{
    internal static void Run()
    {
        const int iterations = 25_000;
        Console.WriteLine("Managed raster replacement/retirement; reused decoded pixels; median of 7 samples, 25,000 operations; no PTY/renderer.");
        foreach (int count in new[] { 1, 2, 8, 64 })
        {
            TerminalScreen screen = new(count + 1, 2);
            TerminalRasterImageSource source = new(1, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 0, 255]);
            TerminalRasterImagePlacement placement = new(1, TerminalRasterImageLayer.BelowText, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1);
            for (int id = 2; id <= count; id++)
                screen.ReplaceRasterImage(new(id, TerminalRasterImageProtocol.Sixel, 1, 1, [0, 0, 0, 255]),
                    new(id, TerminalRasterImageLayer.BelowText, id, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1));
            for (int i = 0; i < iterations; i++) screen.ReplaceRasterImage(source, placement);
            double[] milliseconds = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < 7; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++) screen.ReplaceRasterImage(source, placement);
                milliseconds[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(milliseconds); Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"raster-{count}: {milliseconds[3]:F3} ms; {allocations[3]} allocated bytes; placements={screen.GetRasterImagePlacements().Length}"));
        }
    }
}
