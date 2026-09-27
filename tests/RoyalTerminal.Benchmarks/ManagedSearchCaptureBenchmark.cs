// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class ManagedSearchCaptureBenchmark
{
    internal static void Run()
    {
        Console.WriteLine("Managed search captures: median of 7 samples; setup/cold capture excluded; no worker/PTY/rendering.");
        Console.WriteLine("| Rows | Workload | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---:|---|---:|---:|---:|");
        foreach (int rows in new[] { 1024, 16384 })
        foreach (string workload in new[] { "idle", "tail-edit", "active-first-edit", "history-edit", "tail-wrap", "scroll", "writes-unobserved", "writes-observed" })
        {
            int iterations = workload == "idle" ? 10000 : workload.StartsWith("writes-", StringComparison.Ordinal) ? 1000000 : 128;
            _ = Measure(rows, workload, iterations);
            double[] times = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < times.Length; sample++)
                (times[sample], allocations[sample]) = Measure(rows, workload, iterations);
            Array.Sort(times); Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"| {rows} | {workload} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
        }
    }

    private static (double Milliseconds, long Allocated) Measure(int count, string workload, int iterations)
    {
        TerminalScreen screen = new(8, 4, count - 4);
        while (screen.TotalRows < count) screen.AddRow();
        ManagedSearchSnapshot? capture = workload == "writes-unobserved" ? null : ManagedSearchSnapshot.Capture(screen, null);
        TerminalRow tail = screen.GetRow(count - 1);
        bool writesOnly = workload.StartsWith("writes-", StringComparison.Ordinal);
        // Exclude the first COW detachment from the observer write-overhead pair.
        if (writesOnly) tail[0] = new() { Codepoint = 'x', Width = 1 };
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            switch (workload)
            {
                case "tail-edit":
                case "writes-unobserved":
                case "writes-observed": tail[0] = new() { Codepoint = 'a' + i % 2, Width = 1 }; break;
                case "active-first-edit": screen.GetRow(count - screen.ViewportRows)[0] = new() { Codepoint = 'a' + i % 2, Width = 1 }; break;
                case "history-edit": screen.GetRow(0)[0] = new() { Codepoint = 'a' + i % 2, Width = 1 }; break;
                case "tail-wrap": tail.WrapsToNext = i % 2 == 0; break;
                case "scroll": screen.AddRow()[0] = new() { Codepoint = 'x', Width = 1 }; break;
            }
            if (!writesOnly) capture = ManagedSearchSnapshot.Capture(screen, capture);
        }
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(capture);
        GC.KeepAlive(screen);
        return (milliseconds, allocated);
    }
}
