// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

internal static class ManagedSnapshotUsageBenchmark
{
    internal static void Run()
    {
        Console.WriteLine("Snapshot metadata census only: combined query vs three indexed queries; median of 7 samples; setup excluded.");
        Console.WriteLine("Individual queries also use the new allocation-free revision gate; this isolates pass/lookup reduction, not the former enumerator boxing.");
        Console.WriteLine("| Rows | State | Queries | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---:|---|---|---:|---:|---:|");
        foreach (int count in new[] { 1, 64, 215, 4096 })
        foreach (bool dirty in new[] { false, true })
        {
            GhosttySnapshotPageAllocation page = new(new(1, (ushort)count, 16, 192, 1024, 2048));
            List<TerminalRow> rows = new(count);
            for (int i = 0; i < count; i++) rows.Add(new(1) { SnapshotAllocation = page, SnapshotAllocationRow = i });
            GhosttySnapshotPageTracker tracker = new();
            tracker.InstallReflowPage(page, new(page.Capacity), rows);
            if (dirty) rows[^1][0].Codepoint = 'x';
            int iterations = Math.Max(100, 1000000 / count);
            foreach (bool combined in new[] { false, true })
            {
                _ = Measure(tracker, page, rows, combined, iterations);
                double[] times = new double[7];
                long[] allocations = new long[7];
                for (int sample = 0; sample < times.Length; sample++)
                    (times[sample], allocations[sample]) = Measure(tracker, page, rows, combined, iterations);
                Array.Sort(times); Array.Sort(allocations);
                Console.WriteLine(FormattableString.Invariant($"| {count} | {(dirty ? "dirty-tail" : "current")} | {(combined ? "combined" : "three-indexed")} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
            }
        }
    }

    private static (double Milliseconds, long Allocated) Measure(GhosttySnapshotPageTracker tracker,
        GhosttySnapshotPageAllocation page, List<TerminalRow> rows, bool combined, int iterations)
    {
        ulong checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            if (combined)
            {
                bool current = tracker.TryGetMetadataUsage(page, rows, out GhosttySnapshotMetadataUsage usage);
                checksum += (current ? 1UL : 0) + usage.Styles + usage.GraphemeCells + usage.GraphemeBytes +
                    usage.Hyperlinks + usage.HyperlinkCells + usage.StringBytes;
            }
            else
            {
                bool current = tracker.TryGetStyleUsage(page, rows, out int styles);
                current &= tracker.TryGetGraphemeUsage(page, rows, out ulong cells, out ulong bytes);
                current &= tracker.TryGetHyperlinkUsage(page, rows, out ulong links, out ulong linked, out ulong strings);
                checksum += (current ? 1UL : 0) + (ulong)styles + cells + bytes + links + linked + strings;
            }
        }
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return (milliseconds, allocated);
    }
}
