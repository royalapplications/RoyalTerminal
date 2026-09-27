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
        foreach (string name in new[] { "ascii", "unicode", "wide", "rep", "styled", "snapshot-styled", "edit", "snapshot-edit", "snapshot-erase", "snapshot-grapheme", "dec-special", "save-restore" })
        {
            TerminalScreen screen = new(80, 24, 0);
            bool tracked = name.StartsWith("snapshot-", StringComparison.Ordinal);
            using BasicVtProcessor seed = new(screen, new() { ContinuationMaxBytes = tracked ? 1_048_576 : 0 });
            using ManagedTerminalSnapshot? snapshot = tracked
                ? ManagedTerminalSnapshot.Restore(seed.GetBinarySnapshot()) : null;
            BasicVtProcessor processor = snapshot?.Processor ?? seed;
            screen = snapshot?.Screen ?? screen;
            if (name == "dec-special") processor.Process("\u001b(0"u8);
            byte[] bytes = Encoding.UTF8.GetBytes(name switch
            {
                "save-restore" => "\u001b7\u001b8",
                "unicode" => new string('λ', 79) + "\r",
                "wide" => new string('界', 39) + "\r",
                "rep" => "q\u001b[78b\r",
                "edit" or "snapshot-edit" => new string('q', 79) + "\r\u001b[2@\u001b[2P\r",
                "snapshot-erase" => new string('q', 79) + "\r\u001b[K",
                "snapshot-grapheme" => "A\u0301\u0302\u0303\r",
                "styled" or "snapshot-styled" => "\u001b[31;1m" + new string('q', 79) + "\r\u001b[32;3m" + new string('r', 79) + "\r",
                _ => new string('q', 79) + "\r",
            });
            for (int i = 0; i < iterations; i++) processor.Process(bytes);
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
