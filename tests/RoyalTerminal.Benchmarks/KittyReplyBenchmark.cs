// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Terminal;

internal static class KittyReplyBenchmark
{
    internal static void Run()
    {
        Console.WriteLine("Kitty reply encoding: former StringBuilder vs bounded header/owned array; median of 7 samples.");
        Console.WriteLine("| Reply | Encoder | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---|---|---:|---:|---:|");
        foreach ((string name, uint id, uint number, uint placement, uint frame, string message) in new[]
        {
            ("image", 1u, 0u, 0u, 0u, "OK"),
            ("number", 0u, 12u, 0u, 0u, "ENOENT: image not found"),
            ("placement", 1u, 0u, 2u, 0u, "OK"),
            ("frame", 1u, 2u, 3u, 4u, "OK"),
            ("maximum-fields", uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, "EINVAL: invalid data"),
            ("long-host-error", 1u, 0u, 0u, 0u, new string('x', 4096)),
        })
        {
            byte[] expected = Legacy(id, number, placement, frame, message);
            if (!expected.AsSpan().SequenceEqual(ManagedKittyResponseFormatter.Format(id, number, placement, frame, message)))
                throw new InvalidOperationException("Paired Kitty reply outputs differ.");
            foreach (bool current in new[] { false, true })
            {
                int iterations = message.Length > 100 ? 1000 : 100000;
                _ = Measure(current, id, number, placement, frame, message, iterations);
                double[] times = new double[7];
                long[] allocations = new long[7];
                for (int sample = 0; sample < times.Length; sample++)
                    (times[sample], allocations[sample]) = Measure(current, id, number, placement, frame, message, iterations);
                Array.Sort(times); Array.Sort(allocations);
                Console.WriteLine(FormattableString.Invariant($"| {name} | {(current ? "bounded" : "builder")} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
            }
        }
    }

    private static (double Milliseconds, long Allocated) Measure(bool current, uint id, uint number, uint placement, uint frame,
        string message, int iterations)
    {
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            byte[] response = current ? ManagedKittyResponseFormatter.Format(id, number, placement, frame, message)
                : Legacy(id, number, placement, frame, message);
            checksum += response.Length;
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return (elapsed, allocated);
    }

    private static byte[] Legacy(uint id, uint number, uint placement, uint frame, string message)
    {
        StringBuilder reply = new("\u001b_G");
        if (id != 0) reply.Append("i=").Append(id);
        if (number != 0)
        {
            if (id != 0) reply.Append(',');
            reply.Append("I=").Append(number);
        }
        if (placement != 0) reply.Append(",p=").Append(placement);
        if (frame != 0) reply.Append(",r=").Append(frame);
        reply.Append(';').Append(message).Append("\u001b\\");
        return Encoding.ASCII.GetBytes(reply.ToString());
    }
}
