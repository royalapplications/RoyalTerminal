// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using RoyalTerminal.Terminal;

internal static class KittyImageLoaderBenchmark
{
    // All calls also compile against 317610d3: use the same runner there for
    // before/after measurements. Inputs are prepared outside measured regions.
    internal static void Run()
    {
        Console.WriteLine("Kitty image loading: median of 7 samples; parser, storage, publication and RGB expansion excluded.");
        Console.WriteLine("Compare with the identical runner at 317610d3 (heap-owned accumulation buffer).");
        Console.WriteLine("| Workload | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (string workload in new[] { "tiny-rgba", "tiny-rgb", "bulk-rgba", "bulk-rgb", "chunked", "zlib" })
        {
            int side = workload.StartsWith("tiny", StringComparison.Ordinal) ? 1 : 128;
            int channels = workload.EndsWith("-rgb", StringComparison.Ordinal) ? 3 : 4;
            byte[] pixels = new byte[side * side * channels];
            pixels.AsSpan().Fill(73);
            ManagedKittyGraphicsCommand command = Command($"f={channels * 8},s={side},v={side}" +
                (workload == "zlib" ? ",o=z" : ""));
            ManagedKittyGraphicsCommand? continuation = null;
            if (workload == "zlib")
            {
                using MemoryStream output = new();
                using (ZLibStream stream = new(output, CompressionLevel.Fastest, leaveOpen: true)) stream.Write(pixels);
                command.SetData(output.ToArray());
            }
            else if (workload == "chunked")
            {
                command.SetData(pixels.AsSpan(0, pixels.Length / 2).ToArray());
                continuation = Command("m=0");
                continuation.SetData(pixels.AsMemory(pixels.Length / 2));
            }
            else command.SetData(pixels);
            int iterations = side == 1 ? 100000 : 2000;
            _ = Measure(command, continuation, iterations);
            double[] times = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < times.Length; sample++)
                (times[sample], allocations[sample]) = Measure(command, continuation, iterations);
            Array.Sort(times); Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"| {workload} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
        }
    }

    private static (double Milliseconds, long Allocated) Measure(ManagedKittyGraphicsCommand command,
        ManagedKittyGraphicsCommand? continuation, int iterations)
    {
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            if (!ManagedKittyImageLoader.TryCreate(command, null, 1024 * 1024, out var loader, out string error))
                throw new InvalidOperationException(error);
            if (continuation is not null && !loader.TryAppend(continuation, out error))
                throw new InvalidOperationException(error);
            if (!loader.TryComplete(out var image, out error)) throw new InvalidOperationException(error);
            checksum += image.StorageBytes + image.Width;
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return (elapsed, allocated);
    }

    private static ManagedKittyGraphicsCommand Command(string control)
    {
        if (!ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(control), 0, out var command))
            throw new InvalidOperationException("Benchmark control rejected.");
        return command;
    }
}
