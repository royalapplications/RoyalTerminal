// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Terminal.Snapshots;

internal static class ManagedSnapshotGridBenchmark
{
    internal static void Run()
    {
        const int columns = 257, rows = 64;
        Console.WriteLine("Snapshot grid only: encode to Stream.Null / owned decode; median of 7 samples; setup excluded.");
        Console.WriteLine("| Workload | Operation | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---|---|---:|---:|---:|");
        foreach (string name in new[] { "blank", "ascii", "bmp", "styled", "linked", "wide", "graphemes", "sparse-graphemes" })
        {
            ulong[] cells = new ulong[columns * rows];
            Dictionary<int, uint[]> suffixes = [];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = name switch
                {
                    "blank" => 0,
                    "bmp" => 0x3BBUL << 2,
                    "styled" => ((ulong)'x' << 2) | (1UL << 26),
                    "linked" => ((ulong)'x' << 2) | (1UL << 45) | (1UL << 48),
                    "wide" when i % columns < columns - 1 => i % columns % 2 == 0 ? (0x754CUL << 2) | (1UL << 42) : 2UL << 42,
                    _ => (ulong)'x' << 2,
                };
                if (name == "graphemes" || name == "sparse-graphemes" && i % 127 == 0)
                {
                    cells[i] |= 1;
                    suffixes.Add(i, [0x301, 0x302, 0x1F3FB]);
                }
            }
            GhosttySnapshotGrid grid = GhosttySnapshotGrid.FromOwnedCells(columns, new byte[rows], cells, suffixes);
            using MemoryStream output = new();
            grid.WriteTo(output);
            byte[] encoded = output.ToArray();
            int iterations = name == "graphemes" ? 20 : 100;
            foreach (bool decode in new[] { false, true })
            {
                _ = Measure(grid, encoded, decode, iterations);
                double[] times = new double[7];
                long[] allocations = new long[7];
                for (int sample = 0; sample < times.Length; sample++)
                    (times[sample], allocations[sample]) = Measure(grid, encoded, decode, iterations);
                Array.Sort(times); Array.Sort(allocations);
                Console.WriteLine(FormattableString.Invariant($"| {name} | {(decode ? "decode" : "encode")} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
            }
        }
    }

    private static (double Milliseconds, long Allocated) Measure(GhosttySnapshotGrid grid, byte[] encoded, bool decode, int iterations)
    {
        GhosttySnapshotGrid? last = null;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            if (decode) last = GhosttySnapshotGrid.Read(encoded, grid.Columns, grid.Rows, grid.Cells.Length,
                grid.Cells.Length * 3, out _);
            else grid.WriteTo(Stream.Null);
        }
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(last);
        return (milliseconds, allocated);
    }
}
