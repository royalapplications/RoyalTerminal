// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;

internal static class HyperlinkCollectionBenchmark
{
    internal static void Run()
    {
        const int count = 4096;
        byte[][] uris = new byte[count][];
        for (int index = 0; index < count; index++) uris[index] = Encoding.UTF8.GetBytes($"https://example.test/{index}/" + new string('u', 256));
        Console.WriteLine("Same owned registration path, pressure collection enabled/disabled. Setup and retained-token census excluded; not a parser or historical-version throughput comparison.");
        foreach (int rows in new[] { 24, 240, 2400 })
        foreach (bool collect in new[] { false, true })
        {
            _ = Measure(rows, collect, uris);
            double[] elapsed = new double[7];
            long[] allocated = new long[7];
            int retained = 0;
            for (int sample = 0; sample < elapsed.Length; sample++)
                (elapsed[sample], allocated[sample], retained) = Measure(rows, collect, uris);
            Array.Sort(elapsed);
            Array.Sort(allocated);
            Console.WriteLine($"hyperlink-collection/{rows}/enabled={collect}: registrations={count}; median={elapsed[3]:F3} ms; allocated={allocated[3]} bytes; retained={retained}");
        }
    }

    private static (double Milliseconds, long Allocated, int Retained) Measure(int rows, bool collect, byte[][] uris)
    {
        TerminalScreen screen = new(80, rows);
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int index = 0; index < uris.Length; index++)
        {
            if (collect) screen.CollectUnusedHyperlinks([]);
            int token = screen.RegisterHyperlink(uris[index], [], (uint)index, out _);
            // Keep a small live prefix to measure roots as well as dead churn.
            if (index < 16) screen.GetViewportRow(0).Cells[index].HyperlinkId = token;
        }
        if (collect) screen.CollectUnusedHyperlinks([]);
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        int retained = 0;
        for (int token = 1; token <= uris.Length; token++) if (screen.TryGetHyperlink(token, out _)) retained++;
        GC.KeepAlive(screen);
        return (elapsed, allocated, retained);
    }
}
