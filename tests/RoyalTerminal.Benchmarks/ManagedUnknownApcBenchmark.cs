// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Runtime.InteropServices;
using RoyalTerminal.Terminal;

internal static class ManagedUnknownApcBenchmark
{
    internal static void Run()
    {
        Console.WriteLine("Unknown APC capture: former reusable List vs bounded owner; median of 7 samples, warmed scratch.");
        Console.WriteLine("Parser, continuation and callbacks excluded; both paths return owned bytes. Large scratch release is intentional.");
        Console.WriteLine("| Workload | Capture | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---|---|---:|---:|---:|");
        foreach ((string name, int size, int limit, int chunk) in new[]
        {
            ("small", 32, 4096, 32), ("full", 4096, 4096, 4096),
            ("clipped", 65536, 4096, 65536), ("fragmented", 4096, 4096, 31),
            ("large-exact", 8192, 8192, 8192), ("large-fragmented", 8192, 16384, 31),
        })
        {
            byte[] payload = new byte[size];
            Array.Fill(payload, (byte)'X');
            ManagedUnknownApcCapture capture = new();
            List<byte> legacy = [];
            byte[] expected = Encode(false, ref capture, legacy, payload, limit, chunk);
            byte[] actual = Encode(true, ref capture, legacy, payload, limit, chunk);
            if (!expected.AsSpan().SequenceEqual(actual)) throw new InvalidOperationException("Paired captures differ.");
            foreach (bool current in new[] { false, true })
            {
                int iterations = size > 4096 ? 1000 : 10000;
                _ = Measure(current, payload, limit, chunk, iterations);
                double[] times = new double[7];
                long[] allocations = new long[7];
                for (int sample = 0; sample < times.Length; sample++)
                    (times[sample], allocations[sample]) = Measure(current, payload, limit, chunk, iterations);
                Array.Sort(times); Array.Sort(allocations);
                Console.WriteLine(FormattableString.Invariant($"| {name} | {(current ? "bounded" : "list")} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
            }
        }
    }

    private static (double Milliseconds, long Allocated) Measure(bool current, byte[] payload, int limit, int chunk, int iterations)
    {
        ManagedUnknownApcCapture capture = new();
        List<byte> legacy = [];
        _ = Encode(current, ref capture, legacy, payload, limit, chunk);
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            byte[] content = Encode(current, ref capture, legacy, payload, limit, chunk);
            checksum += content.Length + content[^1];
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return (elapsed, allocated);
    }

    private static byte[] Encode(bool current, ref ManagedUnknownApcCapture capture, List<byte> legacy,
        ReadOnlySpan<byte> payload, int limit, int chunk)
    {
        if (current) capture.Begin(limit);
        else legacy.Clear();
        for (int offset = 0; offset < payload.Length; offset += chunk)
        {
            ReadOnlySpan<byte> part = payload.Slice(offset, Math.Min(chunk, payload.Length - offset));
            if (current) capture.Append(part);
            else
            {
                int retained = Math.Min(part.Length, Math.Max(0, limit - legacy.Count));
                int start = legacy.Count;
                CollectionsMarshal.SetCount(legacy, start + retained);
                part[..retained].CopyTo(CollectionsMarshal.AsSpan(legacy)[start..]);
            }
        }
        return current ? capture.Finish(out _) : legacy.ToArray();
    }
}
