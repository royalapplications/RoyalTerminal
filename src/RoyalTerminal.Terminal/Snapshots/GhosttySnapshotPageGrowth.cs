// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Adapted from Ghostty PageList.growCapacity. See ../THIRD-PARTY-NOTICES.txt.

namespace RoyalTerminal.Terminal.Snapshots;

// Per-screen, copyable history of the last three full pages. Uses allocator
// counters only; growing a page must not scan its cells or retained scrollback.
internal sealed class GhosttySnapshotPageGrowth
{
    private readonly record struct Hint(GhosttySnapshotPageCapacity Need, byte Used);
    private readonly Hint?[] _hints = new Hint?[3];
    private int _next;

    internal GhosttySnapshotPageGrowth Copy()
    {
        GhosttySnapshotPageGrowth copy = new() { _next = _next };
        _hints.CopyTo(copy._hints, 0);
        return copy;
    }

    internal GhosttySnapshotPageCapacity Next(GhosttySnapshotPageCapacity previous,
        GhosttySnapshotPageStorage storage, int liveRows, int columns, GhosttySnapshotAllocation layout)
    {
        GhosttySnapshotPageCapacity standard = layout.InitialCapacity(columns), need = standard;
        byte used = 0;
        for (int index = 0; index < 5; index++)
        {
            GhosttySnapshotCapacityDimension dimension = (GhosttySnapshotCapacityDimension)index;
            ulong actual = dimension == GhosttySnapshotCapacityDimension.HyperlinkBytes
                ? (ulong)storage.Hyperlinks.Count : storage.Usage(dimension);
            if (actual != 0) used |= (byte)(1 << index);
            ulong available = dimension switch
            {
                GhosttySnapshotCapacityDimension.Styles => GhosttySnapshotAllocation.SetItemCapacity(previous.Styles) + (previous.Styles == 0 ? 0UL : 1UL),
                GhosttySnapshotCapacityDimension.GraphemeBytes => GhosttySnapshotAllocation.BitmapDataBytes(previous.GraphemeBytes, 16),
                GhosttySnapshotCapacityDimension.HyperlinkBytes => GhosttySnapshotAllocation.SetItemCapacity(previous.HyperlinkBytes / 48UL) + (previous.HyperlinkBytes < 48 ? 0UL : 1UL),
                GhosttySnapshotCapacityDimension.HyperlinkCells => GhosttySnapshotAllocation.MapItemCapacity(previous.HyperlinkCells, 80),
                _ => GhosttySnapshotAllocation.BitmapDataBytes(previous.StringBytes, 32),
            };
            ulong scaled = liveRows == 0 ? actual : Math.Min(available, actual * standard.Rows / (ulong)liveRows);
            ulong request = scaled + scaled / 2;
            if (dimension == GhosttySnapshotCapacityDimension.Styles) request = SetCapacity(request);
            else if (dimension == GhosttySnapshotCapacityDimension.HyperlinkBytes)
                request = scaled == 0 ? 0 : SetCapacity(Math.Max(32, request)) * 48;
            ulong value = scaled * 3 < available ? request : Get(previous, dimension);
            need = Set(need, dimension, Math.Max(Get(standard, dimension), value));
        }
        GhosttySnapshotPageCapacity capacity = need;
        if (_hints[(_next + 2) % 3] is { } newest)
        {
            for (int index = 0; index < 5; index++)
            {
                GhosttySnapshotCapacityDimension dimension = (GhosttySnapshotCapacityDimension)index;
                ulong value = Get(newest.Need, dimension);
                if (((used | newest.Used) & (1 << index)) != 0)
                    foreach (Hint? hint in _hints)
                        if (hint is { } item) value = Math.Max(value, Get(item.Need, dimension));
                capacity = Set(capacity, dimension, Math.Max(value, Get(capacity, dimension)));
            }
        }
        ulong bytes = layout.LayoutBytes(capacity);
        if (bytes > layout.StandardPageBytes && layout.TryFitRows(capacity, layout.StandardPageBytes, out var fitted) &&
            (ulong)fitted.Rows * bytes * 10 >= (ulong)capacity.Rows * layout.StandardPageBytes * 9)
            capacity = fitted;
        else if (bytes > uint.MaxValue) capacity = standard;
        _hints[_next] = new(need, used);
        _next = (_next + 1) % 3;
        return capacity;
    }

    private static ulong SetCapacity(ulong count) => count == 0 ? 0 : ((count + 1) * 16 + 12) / 13;
    private static ulong Get(GhosttySnapshotPageCapacity capacity, GhosttySnapshotCapacityDimension dimension) => dimension switch
    {
        GhosttySnapshotCapacityDimension.Styles => capacity.Styles,
        GhosttySnapshotCapacityDimension.GraphemeBytes => capacity.GraphemeBytes,
        GhosttySnapshotCapacityDimension.HyperlinkBytes => capacity.HyperlinkBytes,
        GhosttySnapshotCapacityDimension.HyperlinkCells => capacity.HyperlinkCells,
        _ => capacity.StringBytes,
    };
    private static GhosttySnapshotPageCapacity Set(GhosttySnapshotPageCapacity capacity, GhosttySnapshotCapacityDimension dimension, ulong value)
        => dimension switch
        {
            GhosttySnapshotCapacityDimension.Styles => capacity with { Styles = (ushort)Math.Min(ushort.MaxValue, value) },
            GhosttySnapshotCapacityDimension.GraphemeBytes => capacity with { GraphemeBytes = (uint)Math.Min(uint.MaxValue, value) },
            GhosttySnapshotCapacityDimension.HyperlinkBytes => capacity with { HyperlinkBytes = (ushort)Math.Min(ushort.MaxValue, value) },
            GhosttySnapshotCapacityDimension.HyperlinkCells => capacity with { HyperlinkCells = (ushort)Math.Min(ushort.MaxValue, value) },
            _ => capacity with { StringBytes = (uint)Math.Min(uint.MaxValue, value) },
        };
}
