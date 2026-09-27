// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class NativeRenderOverscanBenchmark
{
    // Measures complete adapter extraction when moving the native viewport,
    // not drawing/GPU or input latency. Zero overscan is the existing-path cost
    // baseline, not a functionally equivalent uncached overscan implementation.
    // The expected gain is retained row storage for the overlapping capture;
    // actual end-to-end cache/render improvements require separate measurement.
    internal static void Run()
    {
        if (!GhosttyVtProcessor.IsAvailable())
            throw new InvalidOperationException("Rebuild the pinned native library before running this benchmark.");
        foreach (int height in new[] { 24, 240, 2400 })
        {
            foreach (ushort extra in new ushort[] { 0, 1 })
            {
                TerminalScreen screen = new(80, height, 16);
                using GhosttyVtProcessor processor = new(screen);
                StringBuilder history = new((height + 8) * 32);
                for (int index = 0; index < height + 8; index++)
                {
                    if (index != 0) history.Append("\r\n");
                    history.Append("row ").Append(index).Append(" benchmark text");
                }
                processor.Process(Encoding.UTF8.GetBytes(history.ToString()));
                processor.RenderOverscan = new(extra, extra);
                int iterations = height == 24 ? 500 : height == 240 ? 100 : 20;
                int frame = 0;
                long checksum = 0;
                void Move()
                {
                    processor.SetViewportOffsetRows((ulong)(1 + frame++ % 3));
                    TerminalRenderViewport view = screen.GetRenderViewport(processor.RenderOverscan);
                    checksum += view.Count + view[0].Row.ReadOnlyCells[0].Codepoint;
                }
                for (int index = 0; index < 12; index++) Move();
                double[] times = new double[7];
                long[] allocated = new long[7];
                for (int sample = 0; sample < times.Length; sample++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    for (int index = 0; index < iterations; index++) Move();
                    times[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocated[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                }
                Array.Sort(times);
                Array.Sort(allocated);
                Console.WriteLine($"native mirror/{height}/overscan {extra}: n={iterations}; median={times[3]:F3} ms; managed allocated={allocated[3]} bytes; checksum={checksum}");
            }
        }
    }
}
