// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class FractionalScrollBenchmark
{
    // Separates phase-only updates (retain native cells) from crossing row
    // boundaries (recapture required). Not a GPU or input-to-frame latency test.
    internal static void Run()
    {
        if (!GhosttyVtProcessor.IsAvailable())
            throw new InvalidOperationException("Rebuild the pinned native library before running this benchmark.");
        foreach (int rows in new[] { 24, 240, 2400 })
        {
            TerminalScreen screen = new(80, rows, 10);
            using GhosttyVtProcessor processor = new(screen);
            StringBuilder text = new();
            for (int row = 0; row < rows + 6; row++)
            {
                if (row != 0) text.Append("\r\n");
                text.Append("fractional scroll row ").Append(row);
            }
            processor.Process(Encoding.UTF8.GetBytes(text.ToString()));
            foreach (bool crossRow in new[] { false, true })
            {
                int frame = 0;
                void Move()
                {
                    int phase = frame++ & 1;
                    processor.SetViewportScrollPosition(new((ulong)(crossRow ? phase + 1 : 1), phase == 0 ? 0.25 : 0.75));
                }
                for (int frameIndex = 0; frameIndex < 20; frameIndex++) Move();
                int iterations = rows == 24 ? 500 : rows == 240 ? 100 : 20;
                double[] times = new double[7];
                long[] allocations = new long[7];
                for (int sample = 0; sample < times.Length; sample++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    for (int index = 0; index < iterations; index++) Move();
                    times[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                }
                Array.Sort(times);
                Array.Sort(allocations);
                Console.WriteLine($"fractional scroll/{rows}/cross-row={crossRow}: n={iterations}; median={times[3]:F3} ms; allocated={allocations[3]} bytes; phase={processor.PublishedViewportPosition.FractionalRow}");
            }
        }
    }
}
