// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;

internal static class NativeKittyTextRefreshBenchmark
{
    internal static void Run()
    {
        if (!GhosttyVtProcessor.IsAvailable() || !GhosttyVtHelpers.GetBuildFeatures().KittyGraphics)
            throw new InvalidOperationException("This benchmark requires native Ghostty with Kitty graphics.");
        foreach (int count in new[] { 0, 1, 128 })
        {
            TerminalScreen screen = new(80, 24, 0);
            using GhosttyVtProcessor processor = new(screen);
            processor.NotifyResize(80, 24, 800, 480);
            processor.Process("\x1b_Ga=t,f=32,i=1,s=1,v=1;/wAA/w==\x1b\\"u8);
            for (int index = 1; index <= count; index++)
                processor.Process(Encoding.ASCII.GetBytes($"\x1b_Ga=p,i=1,p={index},c=1,r=1,C=1\x1b\\"));
            const int iterations = 1000;
            for (int i = 0; i < 200; i++) Frame(processor, i);
            double[] times = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < times.Length; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++) Frame(processor, i);
                times[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Array.Sort(times);
            Array.Sort(allocations);
            Console.WriteLine($"Native text refresh/{count} placements: frames={iterations}; median={times[3]:F3} ms; allocated={allocations[3]} bytes; visible={screen.GetKittyPlacements().Length}");
        }
    }

    private static void Frame(GhosttyVtProcessor processor, int frame)
        => processor.Process((frame & 1) == 0 ? "\x1b[24;1HA"u8 : "\x1b[24;1HB"u8);
}
