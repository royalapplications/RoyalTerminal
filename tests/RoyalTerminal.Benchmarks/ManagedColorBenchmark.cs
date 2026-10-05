// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;

internal static class ManagedColorBenchmark
{
    internal static void Run()
    {
        const int iterations = 2_000;
        Console.WriteLine("Managed OSC color publication; median of 7 samples, 2,000 feeds; includes parsing/recolor/invalidation, no renderer/PTY.");
        foreach (string name in new[] { "repeat-palette", "repeat-dynamic", "cursor-with-palette", "palette-change", "palette-reset" })
        {
            TerminalScreen screen = new(80, 24);
            using BasicVtProcessor processor = new(screen, new() { ContinuationMaxBytes = 0 });
            processor.Process("\u001b[38;5;42mA\u001b]4;42;#123456\u001b\\\u001b]12;#abcdef\u001b\\"u8);
            byte[] bytes = Encoding.ASCII.GetBytes(name switch
            {
                "repeat-palette" => "\u001b]4;42;#123456\u001b\\",
                "repeat-dynamic" => "\u001b]12;#abcdef\u001b\\",
                "cursor-with-palette" => "\u001b]12;#123456\u001b\\\u001b]12;#abcdef\u001b\\",
                "palette-change" => "\u001b]4;42;#abcdef\u001b\\\u001b]4;42;#123456\u001b\\",
                _ => "\u001b]104;42\u001b\\\u001b]4;42;#123456\u001b\\",
            });
            for (int i = 0; i < iterations; i++) processor.Process(bytes);
            Measure(name, iterations, () => processor.Process(bytes));
            GC.KeepAlive(screen.Theme);
        }

        // Exercise the shared immutable palette API independently of parser and
        // screen costs; retain only the final result, not 2,000 old palettes.
        TerminalPalette palette = TerminalTheme.Dark.Palette;
        TerminalPalette result = palette;
        for (int i = 0; i < iterations; i++) result = palette.WithColor(42, 0xFF123456);
        Measure("immutable-palette-copy", iterations, () => result = palette.WithColor(42, 0xFF123456));
        GC.KeepAlive(result);
    }

    private static void Measure(string name, int iterations, Action operation)
    {
        double[] milliseconds = new double[7];
        long[] allocations = new long[7];
        for (int sample = 0; sample < 7; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++) operation();
            milliseconds[sample] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(milliseconds); Array.Sort(allocations);
        Console.WriteLine(FormattableString.Invariant($"{name}: {milliseconds[3]:F3} ms; {allocations[3]} allocated bytes"));
    }
}
