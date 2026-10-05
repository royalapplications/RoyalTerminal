// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class ManagedSearchPrependBenchmark
{
    // Compare the identical runner on 23d84027. Capture, restore and the first
    // completed scan are excluded; this isolates resuming after a 64-row prepend.
    internal static void Run()
    {
        foreach (int rows in new[] { 1024, 16384 })
        foreach (string needle in new[] { "needle", "suffix\nneedle", "aaa" })
        {
            TerminalScreen screen = new(40, 4, rows + 64);
            using (BasicVtProcessor writer = new(screen))
                for (int row = 0; row < rows; row++) writer.Process("needle aaaaaaaa suffix\r\n"u8);
            ManagedSearchSnapshot full = ManagedSearchSnapshot.Capture(screen, null);
            ManagedSearchSnapshot old = full.Slice(64);
            _ = Measure(old, full, needle);
            double[] times = new double[7];
            long[] allocations = new long[7];
            int scanned = 0;
            for (int sample = 0; sample < times.Length; sample++)
                (times[sample], allocations[sample], scanned) = Measure(old, full, needle);
            Array.Sort(times);
            Array.Sort(allocations);
            Console.WriteLine($"search-prepend/{rows}/{needle.Replace("\n", "\\n", StringComparison.Ordinal)}: median={times[3]:F3} ms; allocated={allocations[3]} bytes; scanned={scanned} rows");
        }
    }

    private static (double Milliseconds, long Allocated, int Scanned) Measure(
        ManagedSearchSnapshot old, ManagedSearchSnapshot full, string needle)
    {
        ManagedTerminalSearch search = new();
        List<TerminalSearchMatch> results = [];
        search.Populate(old, needle, results);
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        search.Populate(full, needle, results);
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(results);
        return (elapsed, allocated, search.RowsScannedLastSearch);
    }
}
