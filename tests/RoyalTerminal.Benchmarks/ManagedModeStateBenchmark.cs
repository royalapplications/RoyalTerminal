// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Terminal;

internal static class ManagedModeStateBenchmark
{
    internal static void Run()
    {
        Console.WriteLine("Extended DEC mode container only: packed state vs former hash-set/scan representation; median of 7 samples.");
        Console.WriteLine("| Workload | Representation | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---|---|---:|---:|---:|");
        foreach (string workload in new[] { "create-reset", "known-flag", "mode-number", "toggle" })
        foreach (bool packed in new[] { false, true })
        {
            int iterations = workload == "create-reset" ? 10000 : 1000000;
            _ = Measure(workload, packed, iterations);
            double[] times = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < times.Length; sample++)
                (times[sample], allocations[sample]) = Measure(workload, packed, iterations);
            Array.Sort(times); Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"| {workload} | {(packed ? "packed" : "hash-set")} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
        }
    }

    private static (double Milliseconds, long Allocated) Measure(string workload, bool packed, int iterations)
    {
        ManagedDecModeState state = default;
        state.Reset();
        HashSet<int> legacy = [1007, 1035, 1036];
        ReadOnlySpan<int> lookupModes = [3, 12, 1007, 1035, 2027, 5522, 5523, -1];
        int checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            switch (workload)
            {
                case "create-reset":
                    if (packed) { state = default; state.Reset(); checksum += state.Contains(1007) ? 1 : 0; }
                    else { legacy = [1007, 1035, 1036]; checksum += legacy.Contains(1007) ? 1 : 0; }
                    break;
                case "known-flag":
                    // Prevent a constant unchanging flag from replacing the loop.
                    if ((i & 255) == 0)
                    {
                        bool set = (i & 256) == 0;
                        if (packed) state.Set(2027, set);
                        else if (set) legacy.Add(2027); else legacy.Remove(2027);
                    }
                    checksum += (packed ? state.Contains(ManagedDecModeFlag.GraphemeClusters) : legacy.Contains(2027)) ? 1 : 0;
                    break;
                case "mode-number":
                    int mode = lookupModes[i % lookupModes.Length];
                    bool enabled;
                    bool supported = packed ? state.TryGet(mode, out enabled) : LegacyTryGet(legacy, mode, out enabled);
                    checksum += supported ? enabled ? 2 : 1 : 0;
                    break;
                case "toggle":
                    int toggled = ManagedDecModeState.SupportedModes[i % ManagedDecModeState.SupportedModes.Length];
                    bool value = (i & 32) == 0;
                    if (packed) state.Set(toggled, value);
                    else if (value) legacy.Add(toggled); else legacy.Remove(toggled);
                    checksum += (packed ? state.Contains(ManagedDecModeFlag.AlternateScroll) : legacy.Contains(1007)) ? 1 : 0;
                    break;
            }
        }
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        GC.KeepAlive(legacy);
        return (milliseconds, allocated);
    }

    private static bool LegacyTryGet(HashSet<int> enabledModes, int mode, out bool enabled)
    {
        foreach (int known in ManagedDecModeState.SupportedModes)
        {
            if (mode != known) continue;
            enabled = enabledModes.Contains(mode);
            return true;
        }
        enabled = false;
        return false;
    }
}
