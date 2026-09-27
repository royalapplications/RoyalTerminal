// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;

internal static class ManagedSnapshotReflowBenchmark
{
    internal static void Run()
    {
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine("Snapshot-tracked reflow: 512 source rows, 13 resizes; median of 7 samples; setup/restore excluded.");
        Console.WriteLine("| Workload | Median milliseconds | Allocated bytes | Final rows |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (string name in new[] { "plain", "graphemes", "shared-links", "long-shared-links" })
        {
            byte[] snapshot = CreateSnapshot(name);
            _ = Measure(snapshot);
            double[] times = new double[7];
            long[] allocations = new long[7];
            int rows = 0;
            for (int sample = 0; sample < times.Length; sample++)
                (times[sample], allocations[sample], rows) = Measure(snapshot);
            Array.Sort(times);
            Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"| {name} | {times[3]:F3} | {allocations[3]} | {rows} |"));
        }
    }

    private static byte[] CreateSnapshot(string name)
    {
        using BasicVtProcessor source = new(new TerminalScreen(80, 24, 4096));
        if (name.EndsWith("links", StringComparison.Ordinal))
        {
            string uri = name == "long-shared-links" ? "https://example.test/" + new string('u', 1964) : "https://example.test/";
            source.Process(Encoding.UTF8.GetBytes($"\u001b]8;id=shared;{uri}\a"));
        }
        StringBuilder line = new("\u001b[1m");
        for (int column = 0; column < 79; column++)
            line.Append(name == "plain" ? "a" : "a\u0301");
        line.Append("\r\n");
        byte[] input = Encoding.UTF8.GetBytes(line.ToString());
        for (int row = 0; row < 512; row++) source.Process(input);
        source.Process("\u001b]8;;\a\u001b[0m"u8);
        return source.GetBinarySnapshot();
    }

    private static (double Milliseconds, long Allocated, int Rows) Measure(byte[] snapshot)
    {
        using ManagedTerminalSnapshot terminal = ManagedTerminalSnapshot.Restore(snapshot);
        int[] widths = [40, 132, 80, 60, 120, 40, 132, 80, 41, 131, 79, 120, 80];
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        foreach (int width in widths) terminal.Processor.ResizeScreen(width, 24, 0, 0);
        return (Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocated, terminal.Screen.TotalRows);
    }
}
