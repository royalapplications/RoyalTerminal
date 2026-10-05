// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Terminal;

internal static class KittyParserBenchmark
{
    // All APIs used by this harness also exist at 2f5c325f, permitting the
    // identical runner to measure the previous heap-owned parser baseline.
    internal static void Run()
    {
        Console.WriteLine("Kitty command creation/feed/completion: median of 7 samples; image loading and response generation excluded.");
        Console.WriteLine("Run the identical harness against 2f5c325f for the heap-parser baseline.");
        Console.WriteLine("| Workload | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (string workload in new[] { "control", "tiny", "bulk", "fragmented" })
        {
            byte[] input = Encoding.ASCII.GetBytes(workload switch
            {
                "control" => "a=p,i=73,p=1", "tiny" => "i=73;AQIDBA==", _ => "i=73;" + new string('A', 65536),
            });
            int chunk = workload == "fragmented" ? 31 : input.Length;
            int iterations = input.Length < 100 ? 100000 : 1000;
            _ = Measure(input, chunk, iterations);
            double[] times = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < times.Length; sample++)
                (times[sample], allocations[sample]) = Measure(input, chunk, iterations);
            Array.Sort(times); Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"| {workload} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
        }
    }

    private static (double Milliseconds, long Allocated) Measure(byte[] input, int chunk, int iterations)
    {
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            ManagedKittyGraphicsParser parser = new(input.Length);
            for (int offset = 0; offset < input.Length; offset += chunk)
                if (!parser.TryAppend(input.AsSpan(offset, Math.Min(chunk, input.Length - offset))))
                    throw new InvalidOperationException("Benchmark payload rejected.");
            if (!parser.TryComplete(out ManagedKittyGraphicsCommand? command))
                throw new InvalidOperationException("Benchmark completion rejected.");
            checksum += command.ImageId + command.Data.Length;
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return (elapsed, allocated);
    }
}
