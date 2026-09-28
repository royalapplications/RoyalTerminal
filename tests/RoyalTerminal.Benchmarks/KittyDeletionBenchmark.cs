// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class KittyDeletionBenchmark
{
    // Run this same harness against 87004276 for the pre-change baseline.
    // Store/copy/parser setup is excluded: this measures real range deletion,
    // not repeated no-op commands and not end-to-end input/frame latency.
    internal static void Run()
    {
        foreach (int count in new[] { 16, 256, 4096 })
        {
            (TerminalScreen screen, ManagedKittyGraphicsStore store) = Scene(count);
            foreach (bool upper in new[] { false, true })
            {
                _ = Measure(screen, store, count, upper);
                double[] elapsed = new double[7];
                long[] allocated = new long[7];
                for (int sample = 0; sample < elapsed.Length; sample++)
                    (elapsed[sample], allocated[sample]) = Measure(screen, store, count, upper);
                Array.Sort(elapsed);
                Array.Sort(allocated);
                Console.WriteLine($"kitty-delete/{count}/uppercase={upper}: scenes=16; median={elapsed[3]:F3} ms; allocated={allocated[3]} bytes");
            }
        }
    }

    private static (double Milliseconds, long Allocated) Measure(TerminalScreen screen,
        ManagedKittyGraphicsStore store, int count, bool upper)
    {
        const int iterations = 16;
        (TerminalScreen Screen, ManagedKittyGraphicsStore Store)[] copies = new (TerminalScreen, ManagedKittyGraphicsStore)[iterations];
        for (int index = 0; index < iterations; index++) copies[index] = (screen.CreateStateCopy(), store.CreateStateCopy());
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int index = 0; index < iterations; index++)
            copies[index].Store.DeleteByRange(copies[index].Screen, (uint)(count / 4 + 1), (uint)(count * 3 / 4), upper);
        double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        for (int index = 0; index < iterations; index++)
            if (copies[index].Store.PlacementCount != count / 2 ||
                copies[index].Store.ImageCount != (upper ? count / 2 : count))
                throw new InvalidOperationException("Deletion workload retained an unexpected scene.");
        GC.KeepAlive(copies);
        return (elapsed, allocated);
    }

    private static (TerminalScreen, ManagedKittyGraphicsStore) Scene(int count)
    {
        TerminalScreen screen = new(80, 24);
        ManagedKittyGraphicsStore store = new(count * 4);
        ManagedKittyImagePixels pixels = new(new(1, 1, [255, 0, 0, 255]));
        for (uint id = 1; id <= count; id++)
        {
            if (!store.TryAddImage(screen, id, 0, pixels, false, out string error) ||
                !ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes($"a=p,i={id},p=1,c=1,r=1"), 0, out var command) ||
                !store.TryAddPlacement(screen, store.Find(id)!, command, (int)(id % 24), (int)(id % 80), 10, 10, out _, out error))
                throw new InvalidOperationException(error);
        }
        return (screen, store);
    }
}
