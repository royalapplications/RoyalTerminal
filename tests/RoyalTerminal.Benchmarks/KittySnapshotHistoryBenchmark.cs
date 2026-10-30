// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;

internal static class KittySnapshotHistoryBenchmark
{
    internal static void Run()
    {
        TerminalScreen screen = new(80, 24, 20_000);
        for (int row = 0; row < 20_000; row++) screen.AddRow();
        ManagedKittyGraphicsStore store = new(4096);
        for (uint id = 1; id <= 256; id++)
        {
            store.TryAddImage(screen, id, 0, new(new(1, 1, [1, 2, 3, 4])), false, out _);
            for (uint placement = 1; placement <= 4; placement++)
            {
                ManagedKittyGraphicsCommand command = new();
                command.SetValue('p', placement);
                if (placement > 1) { command.SetValue('P', id); command.SetValue('Q', placement - 1); }
                if (!store.TryAddPlacement(screen, store.Find(id)!, command, (int)(id - 1) * 78, 0, 8, 16, out _, out string error))
                    throw new InvalidOperationException(error);
            }
        }
        long cells = 0; int count = 0;
        GhosttySnapshotPagePlan pages = new(screen, 0, new(), ref cells, ref count);
        int emitted = 0;
        PerformanceMeasurement.Header();
        string scenario = $"kitty-snapshot-history-{pages.History.Count}-pages-1024-placements";
        PerformanceMeasurement.Run(scenario, "reference-linear", 40, () =>
        {
            emitted = 0;
            foreach (GhosttySnapshotPagePlan.Range range in pages.History) emitted += CaptureLinear(screen, store, range).Placements.Length;
            if (emitted != 1024) throw new InvalidOperationException("Lost reference placements.");
        }, () => emitted);
        PerformanceMeasurement.Run(scenario, "indexed", 40, () =>
        {
            emitted = 0;
            foreach (GhosttySnapshotKittyPagePlacements page in store.CaptureHistoryPlacements(screen, 0, pages).Values)
                emitted += page.Placements.Length;
            if (emitted != 1024) throw new InvalidOperationException("Lost placements.");
        }, () => emitted);
    }

    // The pre-index implementation retained here makes before/after allocation
    // and timing measurements reproducible in the same process and runtime.
    private static GhosttySnapshotKittyPagePlacements CaptureLinear(TerminalScreen screen, ManagedKittyGraphicsStore store,
        GhosttySnapshotPagePlan.Range range)
    {
        List<GhosttySnapshotKittyPlacement> result = [];
        List<GhosttySnapshotPagePlan.Range> ranges = [range];
        HashSet<ManagedKittyGraphicsStore.PlacementKey> included = [];
        Dictionary<ManagedKittyGraphicsStore.PlacementKey, ManagedKittyGraphicsStore.Placement> placements =
            (Dictionary<ManagedKittyGraphicsStore.PlacementKey, ManagedKittyGraphicsStore.Placement>)store.Placements;
        for (int pass = 0; pass <= store.PlacementCount; pass++)
        {
            bool changed = false;
            foreach (var (key, placement) in placements)
            {
                if (included.Contains(key) || placement.Virtual || placement.Parent is { } parent && !included.Contains(parent)) continue;
                ushort row = 0, column = 0;
                if (placement.Anchor is { } anchor)
                {
                    if (!screen.TryResolveSnapshotAnchor(anchor, 0, out TerminalGridPosition position)) continue;
                    int index = -1;
                    for (int i = 0; i < ranges.Count; i++)
                        if (position.Row >= ranges[i].Start && position.Row < ranges[i].Start + ranges[i].Count) { index = i; break; }
                    if (index < 0) continue;
                    row = (ushort)(position.Row - ranges[index].Start); column = (ushort)position.Column;
                }
                ManagedKittyPlacementOptions options = placement.Options;
                result.Add(new(Key(key), placement.Parent is null ? (byte)0 : (byte)2, 0, row, column,
                    placement.Parent is { } p ? Key(p) : default, placement.HorizontalOffset, placement.VerticalOffset,
                    options.XOffset, options.YOffset, options.SourceX, options.SourceY, options.SourceWidth,
                    options.SourceHeight, options.Columns, options.Rows, options.Z));
                included.Add(key); changed = true;
            }
            if (!changed) break;
        }
        return new(0, [.. result]);
        static GhosttySnapshotKittyKey Key(ManagedKittyGraphicsStore.PlacementKey key)
            => new(key.ImageId, key.Internal ? (byte)0 : (byte)1, key.Id);
    }
}
