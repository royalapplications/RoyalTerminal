// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using RoyalTerminal.Avalonia.Rendering;

internal static class TerminalReflowBenchmark
{
    internal static void Run()
    {
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine("Workload: 2,000 rows; 13 column resizes; median of 7 independent samples after warmup.");
        Console.WriteLine("| Workload | Median milliseconds | Allocated bytes | Final rows |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (string workload in new[] { "ascii", "cjk", "grapheme", "mixed" })
        {
            Measure(workload);
            double[] times = new double[7];
            long[] allocations = new long[7];
            int finalRows = 0;
            for (int sample = 0; sample < times.Length; sample++)
            {
                (times[sample], allocations[sample], finalRows) = Measure(workload);
            }

            Array.Sort(times);
            Array.Sort(allocations);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {workload} | {times[3]:F3} | {allocations[3]} | {finalRows} |"));
        }

        Console.WriteLine("Steady-state scrolling: 50,000 evictions at80 columns, median of7 samples.");
        MeasureScroll();
        double[] scrollTimes = new double[7];
        long[] scrollAllocations = new long[7];
        for (int sample = 0; sample < scrollTimes.Length; sample++)
        {
            (scrollTimes[sample], scrollAllocations[sample]) = MeasureScroll();
        }

        Array.Sort(scrollTimes);
        Array.Sort(scrollAllocations);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Scrolling: {scrollTimes[3]:F3} ms; {scrollAllocations[3]} allocated bytes."));
    }

    private static (double Milliseconds, long Allocated, int FinalRows) Measure(string workload)
    {
        TerminalScreen screen = new(80, 24, scrollbackLimit: 30_000);
        for (int rowIndex = 0; rowIndex < 2_000; rowIndex++)
        {
            TerminalRow row = rowIndex < 24 ? screen.GetRow(rowIndex) : screen.AddRow();
            for (int column = 0; column < 80; column++)
            {
                bool wide = workload == "cjk" || (workload == "mixed" && column % 4 < 2);
                ref TerminalCell cell = ref row[column];
                cell.Codepoint = wide ? 0x754C : 'a' + column % 26;
                cell.Grapheme = workload == "grapheme" ? "a\u0301" : null;
                cell.Width = wide ? (byte)2 : (byte)1;
                if (wide)
                {
                    row[++column].Width = 0;
                }
            }

            row.WrapsToNext = rowIndex % 4 != 3;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        foreach (int columns in new[] { 40, 132, 80, 60, 120, 40, 132, 80, 41, 131, 79, 120, 80 })
        {
            screen.Resize(columns, 24);
        }

        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return (milliseconds, GC.GetAllocatedBytesForCurrentThread() - before, screen.TotalRows);
    }

    private static (double Milliseconds, long Allocated) MeasureScroll()
    {
        TerminalScreen screen = new(80, 24, scrollbackLimit: 1_000);
        for (int row = 0; row < 1_000; row++) screen.AddRow();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int row = 0; row < 50_000; row++) screen.AddRow();
        return (Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
