// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Terminal.Glyphs;

internal static class GlyphReplyBenchmark
{
    internal static void Run()
    {
        Console.WriteLine("Glyph reply encoding: former strings vs bounded bytes; owned results on both paths, median of 7 samples.");
        Console.WriteLine("Parser, glossary mutations, coverage lookup and host callbacks excluded.");
        Console.WriteLine("| Reply | Encoder | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---|---|---:|---:|---:|");
        foreach (string kind in new[] { "register", "register-error", "clear", "clear-error", "query-empty", "query-both" })
        {
            if (!Encode(kind, false).AsSpan().SequenceEqual(Encode(kind, true)))
                throw new InvalidOperationException("Paired glyph reply encoders differ.");
            foreach (bool current in new[] { false, true })
            {
                const int iterations = 100000;
                _ = Measure(kind, current, iterations);
                double[] times = new double[7];
                long[] allocations = new long[7];
                for (int sample = 0; sample < times.Length; sample++)
                    (times[sample], allocations[sample]) = Measure(kind, current, iterations);
                Array.Sort(times); Array.Sort(allocations);
                Console.WriteLine(FormattableString.Invariant($"| {kind} | {(current ? "bounded" : "strings")} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
            }
        }
    }

    private static (double Milliseconds, long Allocated) Measure(string kind, bool current, int iterations)
    {
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            byte[] bytes = Encode(kind, current);
            checksum += bytes.Length + bytes[^1];
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return (elapsed, allocated);
    }

    private static byte[] Encode(string kind, bool current)
    {
        uint codepoint = 0x10FFFD;
        if (kind.StartsWith("query", StringComparison.Ordinal))
        {
            bool both = kind == "query-both";
            if (current) return TerminalGlyphResponseFormatter.Query(codepoint, both, both);
            string status = both ? "system,glossary" : string.Empty;
            return Encoding.ASCII.GetBytes($"\u001b_25a1;q;cp={codepoint:x};status={status}\u001b\\");
        }
        bool register = kind.StartsWith("register", StringComparison.Ordinal);
        string? error = kind.EndsWith("error", StringComparison.Ordinal)
            ? (register ? "out_of_memory" : "out_of_namespace") : null;
        if (current) return TerminalGlyphResponseFormatter.Status(register ? (byte)'r' : (byte)'c', codepoint, error);
        string content = register
            ? error is null ? $"r;cp={codepoint:x};status=0" : $"r;cp={codepoint:x};status=1;reason={error}"
            : error is null ? "c;status=0" : $"c;status=1;reason={error}";
        return Encoding.ASCII.GetBytes("\u001b_25a1;" + content + "\u001b\\");
    }
}
