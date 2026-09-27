// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class RenderRowIdentityBenchmark
{
    internal static void Run()
    {
        foreach (int rows in new[] { 24, 240, 2400 })
        {
            // Actual synchronized-output publication creates new row wrappers
            // sharing unchanged cell storage. All preparation is outside timing.
            TerminalScreen screen = new(80, rows, 0);
            using BasicVtProcessor processor = new(screen);
            TerminalRow[][] frames = new TerminalRow[17][];
            for (int frame = 0; frame < frames.Length; frame++)
            {
                if (frame != 0) processor.Process("\u001b[?2026h\u001b[?2026l"u8);
                TerminalRenderViewport view = screen.GetRenderViewport();
                frames[frame] = new TerminalRow[view.Count];
                for (int index = 0; index < view.Count; index++) frames[frame][index] = view[index].Row;
            }
            if (ReferenceEquals(frames[0][0], frames[1][0]) || frames[0][0].RenderId != frames[1][0].RenderId)
                throw new InvalidOperationException("The cache benchmark requires new wrappers sharing unchanged COW storage.");
            int iterations = rows == 24 ? 200 : rows == 240 ? 50 : 10;
            Measure($"{rows}/wrapper-key baseline", frames, static row => row, iterations);
            Measure($"{rows}/storage-id key", frames, static row => row.RenderId, iterations);
        }
    }

    // Isolates cache-key lookup/insertion and dictionary allocations. Values are
    // integers, not regex output arrays. This is NOT an end-to-end render test.
    // Both sides use the renderer's 32,768-entry cap and retain content checks in
    // production; the benchmark fixtures deliberately have unchanged content.
    private static void Measure<TKey>(string name, TerminalRow[][] frames, Func<TerminalRow, TKey> key, int iterations)
        where TKey : notnull
    {
        int Work()
        {
            Dictionary<TKey, int> cache = new(frames[0].Length);
            int misses = 0;
            foreach (TerminalRow[] frame in frames)
            {
                foreach (TerminalRow row in frame)
                {
                    TKey id = key(row);
                    if (cache.ContainsKey(id)) continue;
                    if (cache.Count >= 32_768) cache.Clear();
                    cache.Add(id, ++misses);
                }
            }
            return misses;
        }

        for (int i = 0; i < 3; i++) Work();
        double[] times = new double[7];
        long[] bytes = new long[7];
        long checksum = 0;
        for (int sample = 0; sample < times.Length; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < iterations; iteration++) checksum += Work();
            times[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            bytes[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(times);
        Array.Sort(bytes);
        Console.WriteLine($"{name}: n={iterations}; median={times[3]:F3} ms; allocated={bytes[3]} bytes; misses checksum={checksum}");
    }
}
