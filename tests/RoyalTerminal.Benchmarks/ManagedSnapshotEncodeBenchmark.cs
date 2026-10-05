// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;

internal static class ManagedSnapshotEncodeBenchmark
{
    internal static void Run()
    {
        Console.WriteLine("Managed snapshot encode to Stream.Null; median of 7 warmed samples; excludes terminal setup and output ownership.");
        foreach (string name in new[] { "blank", "styled", "rich-history", "large-metadata" })
        {
            using BasicVtProcessor processor = new(new TerminalScreen(80, 24, 10_000));
            int rows = name == "rich-history" ? 1_024 : name == "styled" ? 24 : 0;
            byte[] row = Encoding.UTF8.GetBytes(name == "rich-history"
                ? "\u001b[31;1m\u001b]8;id=example;https://example.test\a" + string.Concat(Enumerable.Repeat("é界", 20)) + "\u001b]8;;\a\r\n"
                : "\u001b[31;1m" + new string('x', 79) + "\r\n");
            for (int i = 0; i < rows; i++) processor.Process(row);
            if (name == "large-metadata") processor.SetTitle(Encoding.UTF8.GetBytes(new string('t', 4096)));
            GhosttySnapshotDecodeLimits limits = new();
            int bytes = processor.GetBinarySnapshot(limits).Length;
            int iterations = name == "rich-history" ? 20 : 200;
            for (int i = 0; i < 20; i++) processor.WriteBinarySnapshotTo(Stream.Null, limits);
            double[] milliseconds = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < 7; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++) processor.WriteBinarySnapshotTo(Stream.Null, limits);
                milliseconds[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(milliseconds);
            Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"{name}: {iterations} exports; {bytes} snapshot bytes; {milliseconds[3]:F3} ms; {allocations[3]} allocated bytes"));
        }
    }
}
