// Copyright (c) Royal Apps. Licensed under the MIT license.
using Avalonia;

namespace RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;

/// <summary>Resolve a single presentation action against one immutable visible-window projection. No command changes the split tree.</summary>
internal static class PaneNavigation
{
    internal static Guid? Resolve(IReadOnlyList<PaneSlot> slots, Guid? anchor, string direction)
    {
        if (slots.Count == 0) return null;
        int index = -1;
        for (int i = 0; i < slots.Count; i++) if (slots[i].Block.Id == anchor) { index = i; break; }
        if (direction is "next" or "previous")
        {
            if (index < 0) return anchor.HasValue ? null : slots[direction == "next" ? 0 : slots.Count - 1].Block.Id;
            return slots[(index + slots.Count + (direction == "next" ? 1 : -1)) % slots.Count].Block.Id;
        }
        if (index < 0 || direction is not ("left" or "right" or "up" or "down")) return null;
        bool vertical = direction is "up" or "down";
        int sign = direction is "up" or "left" ? -1 : 1;
        static (double Near, double Far, double CrossNear, double CrossFar) Axes(Rect r, bool v) =>
            v ? (r.Top, r.Bottom, r.Left, r.Right) : (r.Left, r.Right, r.Top, r.Bottom);
        var origin = Axes(slots[index].Area, vertical);
        (int OffAxis, double Distance, double Offset, Guid Id)? best = null;
        for (int i = 0; i < slots.Count; i++)
        {
            if (i == index) continue;
            var rect = Axes(slots[i].Area, vertical);
            double distance = sign * ((rect.Near + rect.Far) - (origin.Near + origin.Far));
            if (distance <= 0) continue;
            int offAxis = Math.Min(origin.CrossFar, rect.CrossFar) > Math.Max(origin.CrossNear, rect.CrossNear) ? 0 : 1;
            var rank = (offAxis, distance, Math.Abs(rect.CrossNear + rect.CrossFar - origin.CrossNear - origin.CrossFar), slots[i].Block.Id);
            if (best is null || rank.CompareTo(best.Value) < 0) best = rank;
        }
        return best?.Id;
    }
}
