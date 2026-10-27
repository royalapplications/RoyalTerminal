// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

internal sealed partial class ManagedKittyGraphicsStore
{
    internal readonly record struct SnapshotPayload(Image Image, int Frame, int Stage, int HistoryPage, bool Displayed, int ImageOrder);

    // One sorted payload array and indexed ranges keep history emission linear
    // in the image/frame count, rather than rescanning all images per page.
    internal sealed class SnapshotPayloadPlan(List<SnapshotPayload> payloads)
    {
        private readonly SnapshotPayload[] _payloads = [.. payloads];
        private readonly Dictionary<(int Stage, int Page), (int Start, int Count)> _ranges = BuildRanges(payloads);

        internal ReadOnlySpan<SnapshotPayload> Get(int stage, int page = -1)
            => _ranges.TryGetValue((stage, page), out var range) ? _payloads.AsSpan(range.Start, range.Count) : [];

        private static Dictionary<(int, int), (int, int)> BuildRanges(List<SnapshotPayload> values)
        {
            Dictionary<(int, int), (int, int)> ranges = [];
            for (int start = 0; start < values.Count;)
            {
                SnapshotPayload first = values[start];
                int end = start + 1;
                while (end < values.Count && values[end].Stage == first.Stage && values[end].HistoryPage == first.HistoryPage) end++;
                ranges.Add((first.Stage, first.HistoryPage), (start, end - start));
                start = end;
            }
            return ranges;
        }
    }

    internal SnapshotPayloadPlan PlanSnapshotPayloads(TerminalScreen screen, int key,
        GhosttySnapshotPagePlan pages, GhosttySnapshotKittyStorage? storage)
    {
        Dictionary<uint, (int Stage, int Page)> locations = [];
        int viewport = screen.GetSnapshotViewportTop(key);
        foreach ((PlacementKey identity, Placement placement) in _placements)
        {
            Placement root = placement;
            int depth = 0;
            bool valid = true;
            while (root.Parent is { } parent)
                if (++depth > _placements.Count || !_placements.TryGetValue(parent, out root!)) { valid = false; break; }
            if (!valid) continue;
            (int Stage, int Page) where = (2, -1);
            if (root.Virtual) where = (0, -1);
            else if (root.Anchor is { } anchor && screen.TryResolveSnapshotAnchor(anchor, key, out TerminalGridPosition cell))
            {
                if (cell.Row >= viewport && cell.Row < viewport + screen.ViewportRows) where = (0, -1);
                else
                    for (int i = pages.History.Count - 1; i >= 0; i--)
                        if (cell.Row >= pages.History[i].Start && cell.Row < pages.History[i].Start + pages.History[i].Count)
                        { where = (1, i); break; }
            }
            else continue;
            if (!locations.TryGetValue(identity.ImageId, out var previous) || where.Stage < previous.Stage ||
                where.Stage == previous.Stage && where.Page > previous.Page) locations[identity.ImageId] = where;
        }
        List<SnapshotPayload> result = [];
        if (storage is not null)
            for (int order = 0; order < storage.Images.Length; order++)
            {
                Image image = _images[storage.Images[order].Id];
                var where = locations.GetValueOrDefault(image.Id, (Stage: 2, Page: -1));
                for (int frame = 0; frame < image.Animation.FrameCount; frame++)
                    if (!image.Animation.FramePixels(frame).IsPending)
                        result.Add(new(image, frame, where.Stage, where.Page, frame == image.Animation.CurrentIndex, order));
            }
        result.Sort(static (a, b) =>
        {
            int value = a.Stage.CompareTo(b.Stage);
            if (value == 0) value = b.HistoryPage.CompareTo(a.HistoryPage);
            if (value == 0) value = b.Displayed.CompareTo(a.Displayed);
            if (value == 0) value = a.ImageOrder.CompareTo(b.ImageOrder);
            return value == 0 ? a.Frame.CompareTo(b.Frame) : value;
        });
        return new(result);
    }
}
