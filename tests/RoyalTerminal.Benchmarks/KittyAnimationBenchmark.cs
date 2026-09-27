// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Terminal;

internal static class KittyAnimationBenchmark
{
    // This runner uses only APIs already present at bb6d33ae. Run the identical
    // file there to compare scalar RGB/fill/blending and redundant canvas setup.
    internal static void Run()
    {
        Console.WriteLine("Kitty RGB views and frame preparation: median of 7 samples; parser, publication and renderer excluded.");
        Console.WriteLine("Compare the identical runner at bb6d33ae; timings/allocation include new per-operation owners.");
        Console.WriteLine("| Workload | Side | Iterations | Milliseconds | Allocated bytes |");
        Console.WriteLine("|---|---:|---:|---:|---:|");
        foreach (int side in new[] { 16, 256 })
        foreach (string workload in new[] { "rgb-view", "background-append", "opaque-blend", "translucent-blend",
                     "transparent-destination", "overwrite-append", "overwrite-base", "overwrite-edit", "clipped-overwrite" })
        {
            byte[] rgb = new byte[side * side * 3];
            byte[] root = new byte[side * side * 4];
            byte[] source = new byte[root.Length];
            Random random = new(592);
            random.NextBytes(rgb); random.NextBytes(root); random.NextBytes(source);
            if (workload == "opaque-blend")
                for (int i = 3; i < source.Length; i += 4) source[i] = 255;
            if (workload == "transparent-destination")
                for (int i = 3; i < root.Length; i += 4) root[i] = 0;
            ManagedKittyImagePixels rootPixels = new(new KittyGraphicsDecodedImage(side, side, root));
            KittyGraphicsDecodedImage frame = new(side, side, source);
            ManagedKittyGraphicsCommand command = Command(workload switch
            {
                "background-append" => $"a=f,x={side},Y=4278190335",
                "opaque-blend" or "translucent-blend" or "transparent-destination" => "a=f,r=1",
                "overwrite-base" => "a=f,c=1,X=1",
                "overwrite-edit" => "a=f,r=1,X=1",
                "clipped-overwrite" => "a=f,r=1,X=1,x=1,y=1",
                _ => "a=f,X=1",
            });
            int iterations = side == 16 ? 5000 : 64;
            _ = Measure(workload == "rgb-view", rgb, rootPixels, frame, command, iterations);
            double[] times = new double[7];
            long[] allocations = new long[7];
            for (int sample = 0; sample < times.Length; sample++)
                (times[sample], allocations[sample]) = Measure(workload == "rgb-view", rgb, rootPixels, frame, command, iterations);
            Array.Sort(times); Array.Sort(allocations);
            Console.WriteLine(FormattableString.Invariant($"| {workload} | {side} | {iterations} | {times[3]:F3} | {allocations[3]} |"));
        }
    }

    private static (double Milliseconds, long Allocated) Measure(bool rgbView, byte[] rgb, ManagedKittyImagePixels root,
        KittyGraphicsDecodedImage source, ManagedKittyGraphicsCommand command, int iterations)
    {
        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++)
        {
            if (rgbView)
            {
                ManagedKittyImagePixels pixels = ManagedKittyImagePixels.FromRgb(root.Width, root.Height, rgb);
                checksum += pixels.GetRgbaImage().Rgba[^1];
            }
            else
            {
                ManagedKittyAnimation animation = new(root);
                if (!animation.TryTransmitFrame(command, source, root.StorageBytes * 2L, out uint number, out string error))
                    throw new InvalidOperationException(error);
                checksum += number + animation.StoredBytes;
            }
        }
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return (elapsed, allocated);
    }

    private static ManagedKittyGraphicsCommand Command(string control)
    {
        if (!ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(control), 0, out var command))
            throw new InvalidOperationException("Benchmark control rejected.");
        return command;
    }
}
