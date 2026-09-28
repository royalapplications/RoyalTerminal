// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Terminal;

internal static class ManagedTabStopsBenchmark
{
    internal static void Run()
    {
        Console.WriteLine("Tabstop container only: former hash/column-scan/sort paths vs packed words; median of 7 samples.");
        Console.WriteLine("Both representations use identical corrected endpoint defaults. Parser, screen and output-string costs are excluded.");
        Console.WriteLine("| Columns | Workload | Representation | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---:|---|---|---:|---:|---:|");
        foreach (int columns in new[] { 80, 512, 521, 4096 })
        foreach (string workload in new[] { "create-reset", "sparse-forward", "sparse-backward", "ordered-stops", "packed-export" })
        foreach (bool packed in new[] { false, true })
        {
            int iterations = Math.Max(1000, 1000000 / columns);
            _ = Measure(columns, workload, packed, iterations);
            double[] times = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < times.Length; sample++)
                (times[sample], allocations[sample]) = Measure(columns, workload, packed, iterations);
            Array.Sort(times); Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"| {columns} | {workload} | {(packed ? "packed" : "hash-set")} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
        }
    }

    private static (double Milliseconds, long Allocated) Measure(int columns, string workload, bool packed, int iterations)
    {
        ManagedTabStops stops = new(columns);
        stops.ResetDefaults();
        HashSet<int> legacy = CreateLegacy(columns);
        if (workload.StartsWith("sparse-", StringComparison.Ordinal))
        {
            stops.Clear(); legacy.Clear();
            stops.Add(columns - 2); legacy.Add(columns - 2);
            stops.Add(1); legacy.Add(1);
        }
        Span<byte> scratch = stackalloc byte[64];
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            switch (workload)
            {
                case "create-reset":
                    if (packed) { stops = new(columns); stops.ResetDefaults(); checksum += stops.Contains(8) ? 1 : 0; }
                    else { legacy = CreateLegacy(columns); checksum += legacy.Contains(8) ? 1 : 0; }
                    break;
                case "sparse-forward":
                    if (packed) checksum += stops.FindNext(2, columns - 1);
                    else
                        for (int c = 2; c < columns; c++)
                            if (legacy.Contains(c)) { checksum += c; break; }
                    break;
                case "sparse-backward":
                    if (packed) checksum += stops.FindPrevious(columns - 3, 0);
                    else
                        for (int c = columns - 3; c >= 0; c--)
                            if (legacy.Contains(c)) { checksum += c; break; }
                    break;
                case "ordered-stops":
                    if (packed)
                    {
                        for (int c = stops.FindNext(0, columns - 1); c >= 0; c = stops.FindNext(c + 1, columns - 1)) checksum += c;
                    }
                    else
                    {
                        int[] sorted = new int[legacy.Count];
                        legacy.CopyTo(sorted); Array.Sort(sorted);
                        foreach (int c in sorted) checksum += c;
                    }
                    break;
                case "packed-export":
                    if (packed)
                    {
                        for (int offset = 0; offset < stops.PackedByteLength; offset += scratch.Length)
                        {
                            int length = Math.Min(scratch.Length, stops.PackedByteLength - offset);
                            stops.CopyPackedBytes(offset, scratch[..length]);
                            for (int b = 0; b < length; b++) checksum += scratch[b];
                        }
                    }
                    else
                    {
                        byte[] bytes = new byte[(columns + 7) / 8];
                        foreach (int c in legacy) bytes[c / 8] |= (byte)(1 << (c % 8));
                        foreach (byte b in bytes) checksum += b;
                    }
                    break;
            }
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum); GC.KeepAlive(stops); GC.KeepAlive(legacy);
        return (elapsed, allocated);
    }

    private static HashSet<int> CreateLegacy(int columns)
    {
        HashSet<int> stops = [];
        for (int c = 8; c < columns - 1; c += 8) stops.Add(c);
        return stops;
    }
}
