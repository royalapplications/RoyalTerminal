// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;

internal static class SnapshotPaletteBenchmark
{
    // Run this identical harness on 0dbb7d0e for the former copying baseline.
    // Fresh color-state owners prevent repeated installs from hiding first-use
    // custom palette allocations. Owner creation and snapshot parsing excluded.
    internal static void Run()
    {
        TerminalTheme custom = Custom(0xFF123456), other = Custom(0xFF345678), hostOther = Custom(0xFFABCDEF);
        foreach (string kind in new[] { "canonical", "host", "owned" })
        foreach (bool overrides in new[] { false, true })
        {
            TerminalTheme source = kind == "canonical" ? TerminalTheme.Dark : custom;
            TerminalTheme host = kind == "host" ? custom : hostOther;
            using BasicVtProcessor writer = new(new TerminalScreen(8, 2));
            writer.ApplyTheme(source);
            if (overrides) writer.Process("\u001b]4;255;#abcdef\a"u8);
            using GhosttySnapshotStateReader reader = new(writer.GetBinarySnapshot(), new());
            GhosttySnapshotTerminalState state = reader.ReadReady().Terminal;
            _ = Measure(state, other, host);
            double[] elapsed = new double[7];
            long[] allocated = new long[7];
            for (int sample = 0; sample < elapsed.Length; sample++)
                (elapsed[sample], allocated[sample]) = Measure(state, other, host);
            Array.Sort(elapsed);
            Array.Sort(allocated);
            Console.WriteLine($"snapshot-palette/{kind}/overrides={overrides}: installs=1000; median={elapsed[3]:F3} ms; allocated={allocated[3]} bytes");
        }
    }

    private static (double Milliseconds, long Allocated) Measure(GhosttySnapshotTerminalState state, TerminalTheme configured, TerminalTheme host)
    {
        ManagedTerminalColors[] owners = new ManagedTerminalColors[1000];
        for (int index = 0; index < owners.Length; index++) owners[index] = new(configured);
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int index = 0; index < owners.Length; index++)
        {
            owners[index].InstallSnapshot(state, host);
            _ = owners[index].GetEffectiveTheme();
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(owners);
        return (elapsed, allocated);
    }

    private static TerminalTheme Custom(uint color)
    {
        uint[] entries = TerminalTheme.Dark.Palette.ToArray();
        entries[7] = color;
        return TerminalTheme.Dark.WithPalette(new TerminalPalette(entries));
    }
}
