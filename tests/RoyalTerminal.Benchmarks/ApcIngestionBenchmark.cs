// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class ApcIngestionBenchmark
{
    internal static void Run()
    {
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine("APC ingestion only: 4 MiB payload, 25 feeds/sample, median of 7; continuation disabled. Abort/decoding outside timing.");
        byte[] payload = new byte[4 * 1024 * 1024];
        Array.Fill(payload, (byte)'A');
        foreach (bool kitty in new[] { false, true })
        {
            using BasicVtProcessor processor = new(new TerminalScreen(80, 24), new() { ContinuationMaxBytes = 0 });
            byte[] prefix = kitty ? "\u001b_G"u8.ToArray() : "\u001b_x"u8.ToArray();
            processor.Process(prefix); processor.Process(payload); processor.Process("\u0018"u8);
            double[] milliseconds = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < 7; sample++)
                for (int iteration = 0; iteration < 25; iteration++)
                {
                    processor.Process(prefix);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    processor.Process(payload);
                    milliseconds[sample] += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocations[sample] += GC.GetAllocatedBytesForCurrentThread() - before;
                    processor.Process("\u0018"u8);
                }
            Array.Sort(milliseconds); Array.Sort(allocations);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{(kitty ? "Kitty buffer" : "Unknown truncated")}: {milliseconds[3]:F3} ms; {allocations[3]} allocated bytes"));
        }
    }
}
