// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class KittyOverscanProjectionBenchmark
{
    // Warm immutable projection reuse, including placeholder scanning. A frame
    // makes two queries: viewport+viewport (cost baseline), or viewport+overscan
    // (the common visibility-check/render pattern). Not a GPU/frame-time test.
    internal static void Run()
    {
        foreach (int height in new[] { 24, 240, 2400 })
        {
            TerminalScreen screen = new(80, height, 8);
            using BasicVtProcessor processor = new(screen);
            processor.NotifyResize(80, height, 800, height * 10);
            processor.Process("\u001b_Ga=T,f=32,i=1,p=7,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\\u001b[?2027h\u001b[38;5;1m"u8);
            StringBuilder text = new();
            for (int index = 0; index < height + 4; index++)
            {
                if (index != 0) text.Append("\r\n");
                text.Append("\U0010EEEE");
            }
            processor.Process(Encoding.UTF8.GetBytes(text.ToString()));
            screen.ScrollOffset = 2;
            foreach (ushort extra in new ushort[] { 0, 1 })
            {
                TerminalRenderOverscan request = new(extra, extra);
                int iterations = height == 24 ? 500 : height == 240 ? 100 : 20;
                int Frame() => screen.GetKittyPlacements().Length + screen.GetKittyPlacements(request).Length;
                for (int index = 0; index < 100; index++) Frame();
                double[] times = new double[7];
                long[] allocations = new long[7];
                long checksum = 0;
                for (int sample = 0; sample < times.Length; sample++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    for (int index = 0; index < iterations; index++) checksum += Frame();
                    times[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                }
                Array.Sort(times);
                Array.Sort(allocations);
                Console.WriteLine($"Kitty projection/{height}/extra {extra}: frames={iterations}; median={times[3]:F3} ms; allocated={allocations[3]} bytes; checksum={checksum}");
            }
        }
    }
}
