// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;

internal static class SelectionProjectionBenchmark
{
    internal static void Run()
    {
        foreach (int history in new[] { 32, 4096, 65_536 })
        {
            TerminalHighlightSpan[] spans = new TerminalHighlightSpan[history];
            for (int i = 0; i < spans.Length; i++) spans[i] = new(i, 0, 79, TerminalHighlightKind.Selection);
            int top = history / 2, rows = 24;
            int iterations = history < 100 ? 10_000 : 100;
            Measure($"{history}/history-sized baseline", () => PreviousProjection(spans, top, rows), iterations);
            Measure($"{history}/visible-only result", () => TerminalSelectionProjection.ProjectViewport(spans, top, rows), iterations);
        }
    }

    // The previous TerminalControl projection, isolated from rendering/clipboard
    // work. Both sides preserve order and include duplicate row spans.
    private static TerminalHighlightSpan[] PreviousProjection(TerminalHighlightSpan[] spans, int top, int rows)
    {
        TerminalHighlightSpan[] result = new TerminalHighlightSpan[spans.Length];
        int count = 0;
        foreach (TerminalHighlightSpan span in spans)
        {
            int row = span.Row - top;
            if (row < 0 || row >= rows) continue;
            result[count++] = new(row, span.StartColumn, span.EndColumn, span.Kind);
        }
        if (count == 0) return Array.Empty<TerminalHighlightSpan>();
        if (count != result.Length) Array.Resize(ref result, count);
        return result;
    }

    private static void Measure(string name, Func<TerminalHighlightSpan[]> project, int iterations)
    {
        for (int i = 0; i < 10; i++) GC.KeepAlive(project());
        double[] milliseconds = new double[7];
        long[] allocations = new long[7];
        long checksum = 0;
        for (int sample = 0; sample < milliseconds.Length; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                TerminalHighlightSpan[] visible = project();
                checksum += visible.Length;
                if (visible.Length != 0) checksum += visible[^1].Row;
                GC.KeepAlive(visible);
            }
            milliseconds[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(milliseconds);
        Array.Sort(allocations);
        Console.WriteLine($"{name}: n={iterations}; median={milliseconds[3]:F3} ms; allocated={allocations[3]} bytes; checksum={checksum}");
    }
}
