// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class ManagedPrintBenchmark
{
    internal static void Run()
    {
        const int iterations = 25_000;
        Console.WriteLine("Managed print/state only; no renderer/PTY; median of 7 samples, 25,000 feeds; warmed, zero scrollback.");
        foreach (string name in new[] { "ascii", "dec-special", "save-restore" })
        {
            TerminalScreen screen = new(80, 24, 0);
            using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0 });
            if (name == "dec-special") processor.Process("\u001b(0"u8);
            byte[] bytes = name == "save-restore" ? "\u001b7\u001b8"u8.ToArray() : Encoding.ASCII.GetBytes(new string('q', 79) + "\r");
            for (int i = 0; i < 2000; i++) processor.Process(bytes);
            double[] milliseconds = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < 7; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++) processor.Process(bytes);
                milliseconds[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(milliseconds); Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"{name}: {milliseconds[3]:F3} ms; {allocations[3]} allocated bytes; first-cell={screen.GetViewportRow(0)[0].Codepoint}"));
        }
    }
}
