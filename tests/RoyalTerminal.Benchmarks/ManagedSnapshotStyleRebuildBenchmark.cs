// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

internal static class ManagedSnapshotStyleRebuildBenchmark
{
    internal static void Run()
    {
        const int iterations = 200;
        Console.WriteLine("Logical snapshot style rebuild: 200 copies, median of 7 samples; fixture setup excluded; no rendering/PTY.");
        Console.WriteLine("| Workload | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---|---:|---:|");
        foreach (string name in new[] { "uniform", "alternating", "rotated-inline", "sparse-pooled" })
        {
            GhosttySnapshotStyleStorage source = new(64);
            GhosttySnapshotStyle bold = new(default, default, default, 1), italic = new(default, default, default, 2);
            bool sparse = name == "sparse-pooled";
            int count = sparse ? 257 : 16384;
            for (int i = count - 1; i >= 0; i--)
            {
                int cell = sparse ? i * 4096 + 255 : i;
                GhosttySnapshotStyle style = name == "alternating" && i % 2 == 0 || name == "rotated-inline" && i / 73 % 2 == 0
                    ? italic : bold;
                if (source.ChangeCell(cell, style) != GhosttySnapshotSetAddResult.Success)
                    throw new InvalidOperationException("Benchmark fixture cannot fit its metadata.");
                if (name == "rotated-inline" && i % 127 == 0) source.ObserveInlineBackground(cell, new(1, 5, 0, 0));
            }
            GhosttySnapshotPageRemap? remap = null;
            if (name == "rotated-inline")
            {
                TerminalRow[] rows = new TerminalRow[64];
                for (int i = 0; i < rows.Length; i++) rows[i] = new(256) { SnapshotAllocationRow = 63 - i };
                remap = new(256, 256, rows);
            }
            _ = Measure(source, remap, iterations);
            double[] times = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < times.Length; sample++)
                (times[sample], allocations[sample]) = Measure(source, remap, iterations);
            Array.Sort(times); Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"| {name} | {times[3]:F3} | {allocations[3]} |"));
        }
    }

    private static (double Milliseconds, long Allocated) Measure(GhosttySnapshotStyleStorage source,
        GhosttySnapshotPageRemap? remap, int iterations)
    {
        GhosttySnapshotStyleStorage? last = null;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
            if (source.Rebuild(64, out last, remap) != GhosttySnapshotSetAddResult.Success)
                throw new InvalidOperationException("Style rebuild failed.");
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(last);
        return (milliseconds, allocated);
    }
}
