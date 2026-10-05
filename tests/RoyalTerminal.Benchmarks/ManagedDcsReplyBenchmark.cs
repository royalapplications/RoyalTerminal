// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class ManagedDcsReplyBenchmark
{
    internal static void Run()
    {
        Console.WriteLine("Owned DCS reply encoding: former strings/lists vs bounded bytes/spans; median of 7 samples.");
        Console.WriteLine("Parser, callbacks, batch copying and frozen-map initialization excluded; both paths allocate the required owned reply array.");
        Console.WriteLine("| Workload | Encoder | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---|---|---:|---:|---:|");
        foreach (string workload in new[] { "sgr-default", "sgr-palette", "sgr-rgb", "terminfo-color", "terminfo-boolean", "terminfo-name" })
        {
            TerminalCell pen = Pen(workload);
            byte[] expected = Legacy(workload, in pen), actual = Current(workload, in pen);
            if (!expected.AsSpan().SequenceEqual(actual)) throw new InvalidOperationException("Paired encoder outputs differ.");
            foreach (bool current in new[] { false, true })
            {
                const int iterations = 100000;
                _ = Measure(workload, current, iterations);
                double[] times = new double[7];
                long[] allocations = new long[7];
                for (int sample = 0; sample < times.Length; sample++)
                    (times[sample], allocations[sample]) = Measure(workload, current, iterations);
                Array.Sort(times); Array.Sort(allocations);
                Console.WriteLine(FormattableString.Invariant($"| {workload} | {(current ? "bounded" : "strings")} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
            }
        }
    }

    private static (double Milliseconds, long Allocated) Measure(string workload, bool current, int iterations)
    {
        TerminalCell pen = Pen(workload);
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            byte[] response = current ? Current(workload, in pen) : Legacy(workload, in pen);
            checksum += response.Length + response[^1];
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return (elapsed, allocated);
    }

    private static TerminalCell Pen(string workload) => workload switch
    {
        "sgr-palette" => new() { Attributes = CellAttributes.Bold, UnderlineStyle = TerminalUnderlineStyle.Curly,
            ForegroundIdentity = TerminalColorIdentity.Palette(255), BackgroundIdentity = TerminalColorIdentity.Palette(12) },
        "sgr-rgb" => new() { Attributes = CellAttributes.Bold, UnderlineStyle = TerminalUnderlineStyle.Curly,
            ForegroundIdentity = TerminalColorIdentity.Rgb(0xFEDCBA), BackgroundIdentity = TerminalColorIdentity.Rgb(0x012345) },
        _ => default,
    };

    private static byte[] Current(string workload, in TerminalCell pen)
    {
        if (workload.StartsWith("sgr-", StringComparison.Ordinal))
        {
            Span<byte> buffer = stackalloc byte[ManagedStatusReplyFormatter.MaximumBytes];
            int length = ManagedStatusReplyFormatter.Sgr(buffer, in pen);
            return buffer[..length].ToArray();
        }
        ReadOnlySpan<byte> key = workload switch
        {
            "terminfo-color" => "436f"u8,
            "terminfo-boolean" => "4158"u8,
            _ => "544e"u8,
        };
        _ = GhosttyXtgettcap.TryCreateResponse(key, "xterm-ghostty", out byte[] response);
        return response;
    }

    private static byte[] Legacy(string workload, in TerminalCell pen)
    {
        if (!workload.StartsWith("sgr-", StringComparison.Ordinal))
        {
            ReadOnlySpan<char> key = workload switch
            {
                "terminfo-color" => "436f", "terminfo-boolean" => "4158", _ => "544e",
            };
            string normalized = key.ToString().ToUpperInvariant();
            // Exact values for this small benchmark subset; the real old path
            // additionally performed a frozen dictionary lookup for non-TN keys.
            string? value = normalized == "544E" ? Convert.ToHexString(Encoding.UTF8.GetBytes("xterm-ghostty"))
                : normalized == "4158" ? null : "323536";
            string suffix = value is null ? string.Empty : $"={value}";
            return Encoding.ASCII.GetBytes($"\u001bP1+r{normalized}{suffix}\u001b\\");
        }
        List<string> parameters = ["0"];
        if ((pen.Attributes & CellAttributes.Bold) != 0) parameters.Add("1");
        if (pen.UnderlineStyle != TerminalUnderlineStyle.None) parameters.Add($"4:{(int)pen.UnderlineStyle}");
        AppendColor(parameters, 30, pen.ForegroundIdentity);
        AppendColor(parameters, 40, pen.BackgroundIdentity);
        string payload = $"{string.Join(';', parameters)}m";
        return Encoding.ASCII.GetBytes($"\u001bP1$r{payload}\u001b\\");
    }

    private static void AppendColor(List<string> parameters, int basis, TerminalColorIdentity color)
    {
        uint value = color.Value;
        if (color.Kind == TerminalColorKind.Palette)
        {
            if (value < 16) parameters.Add((basis + (value < 8 ? value : value + 52)).ToString(CultureInfo.InvariantCulture));
            else parameters.Add($"{basis + 8}:5:{value}");
        }
        else if (color.Kind == TerminalColorKind.Rgb)
            parameters.Add($"{basis + 8}:2::{(value >> 16) & 255}:{(value >> 8) & 255}:{value & 255}");
    }
}
