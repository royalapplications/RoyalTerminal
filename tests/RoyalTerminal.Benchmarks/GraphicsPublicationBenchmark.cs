// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;

internal static class GraphicsPublicationBenchmark
{
    // This runner uses APIs also present at b340f5a1. Use the identical runner
    // against that baseline and the new commit for before/after measurements.
    internal static void Run()
    {
        Console.WriteLine("Graphics publication: warmed registry/projection, 2000 updates/sample, median of 7.");
        Console.WriteLine("Scene inputs/pixels and initial projection excluded. Run the same harness against b340f5a1 for the baseline.");
        Console.WriteLine("| Images | Publication | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---:|---|---:|---:|---:|");
        foreach (int count in new[] { 1, 32, 256 })
        foreach (bool pixelOnly in new[] { false, true })
        {
            TerminalScreen screen = new(80, 24);
            TerminalScreenAnchor anchor = screen.CreateAnchor(0, 0);
            TerminalKittyImageSource[] first = new TerminalKittyImageSource[count], second = new TerminalKittyImageSource[count];
            TerminalKittyImagePlacement[] fixedPlacements = new TerminalKittyImagePlacement[count];
            TerminalKittyAnchoredPlacement[] anchored = new TerminalKittyAnchoredPlacement[count];
            for (int i = 0; i < count; i++)
            {
                first[i] = new(i + 1, 1, 1, [255, 0, 0, 255]);
                second[i] = new(i + 1, 1, 1, [0, 0, 255, 255]);
                fixedPlacements[i] = new(i + 1, TerminalKittyImageLayer.AboveText, i % 80, i / 80,
                    0, 0, 10, 10, 0, 0, 1, 1, 10, 10);
                anchored[i] = new(anchor, i % 80, i / 80, 1, 1, fixedPlacements[i]);
            }
            void Publish(TerminalKittyImageSource[] images)
            {
                if (pixelOnly) screen.ReplaceAnchoredKittyGraphics(images, anchored, [], [], 10, 10);
                else screen.ReplaceKittyGraphics(images, fixedPlacements);
            }
            for (int i = 0; i < 4; i++) Publish(first);
            _ = screen.GetKittyPlacements();
            const int iterations = 2000;
            for (int i = 0; i < iterations; i++) Publish((i & 1) == 0 ? first : second);
            double[] elapsed = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < elapsed.Length; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < iterations; i++) Publish((i & 1) == 0 ? first : second);
                elapsed[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            if (!screen.TryGetKittyImageSource(1, out TerminalKittyImageSource? last) || !ReferenceEquals(last, second[0]))
                throw new InvalidOperationException("Publication lost the final frame.");
            Array.Sort(elapsed); Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"| {count} | {(pixelOnly ? "pixel-only" : "full-scene")} | {iterations} | {elapsed[3]:F3} | {allocations[3]} |"));
        }
    }
}
