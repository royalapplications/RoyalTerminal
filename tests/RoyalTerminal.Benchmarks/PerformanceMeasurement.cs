// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;

// Fixed-work measurements, with setup/JIT warmup and collection outside timing.
// Run revisions serially in alternating order; allocations exclude native memory.
internal static class PerformanceMeasurement
{
    internal static void Header()
        => Console.WriteLine("scenario,engine,iterations,median_ms,min_ms,max_ms,allocated_bytes,checksum");

    internal static void Run(string scenario, string engine, int iterations, Action operation, Func<long> checksum)
    {
        for (int i = 0; i < iterations; i++) operation();
        double[] times = new double[7];
        long[] bytes = new long[7];
        for (int sample = 0; sample < times.Length; sample++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) operation();
            times[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            bytes[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(times);
        Array.Sort(bytes);
        Console.WriteLine(FormattableString.Invariant(
            $"{scenario},{engine},{iterations},{times[3]:F4},{times[0]:F4},{times[^1]:F4},{bytes[3]},{checksum()}"));
    }
}
