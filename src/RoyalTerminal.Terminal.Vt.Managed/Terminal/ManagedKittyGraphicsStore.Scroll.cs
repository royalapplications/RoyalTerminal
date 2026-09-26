// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

internal sealed partial class ManagedKittyGraphicsStore
{
    private readonly List<MarginScrollRestore> _marginScrollRestores = [];

    // Marginless SD copies text into fixed native rows (pins stay put).
    // A no-history SU/IND uses PageList.eraseRow: pins below the erased row
    // move upward, but pins already on row zero survive without cropping.
    // The host's generic text anchors instead follow/discard row contents.
    internal void BeginPinScroll(TerminalScreen screen, int delta, bool resetTopColumn)
    {
        _marginScrollRestores.Clear();
        int activeTop = screen.GetAbsoluteRowForViewportRow(0);
        foreach (Placement placement in _placements.Values)
        {
            if (placement.Anchor is not { } anchor || !screen.TryResolveAnchor(anchor, out var origin)) continue;
            int row = origin.Row - activeTop;
            if (row < 0 || row >= screen.ViewportRows) continue;
            // IND's bounded rotation additionally resets a pin already at
            // the physical origin to column zero; SU's eraseRow does not.
            int column = resetTopColumn && row == 0 ? 0 : origin.Column;
            _marginScrollRestores.Add(new(anchor, column, Math.Max(0, row + delta)));
        }
    }

    // ED3 uses PageList.eraseHistory, whose removed pins migrate to the
    // first surviving page without becoming garbage (including whole-page
    // erasure). Preserve this independently of generic host text anchors.
    internal bool BeginHistoryErase(TerminalScreen screen)
    {
        _marginScrollRestores.Clear();
        int activeTop = Math.Max(0, screen.TotalRows - screen.ViewportRows);
        if (activeTop == 0) return false;
        foreach (Placement placement in _placements.Values)
        {
            if (placement.Anchor is not { } anchor || !screen.TryResolveAnchor(anchor, out var origin)) continue;
            int row = origin.Row - activeTop;
            _marginScrollRestores.Add(new(anchor, row < 0 ? 0 : origin.Column, Math.Max(0, row)));
        }
        return _marginScrollRestores.Count > 0;
    }

    // Mirrors ImageStorage.scrollMarginsBegin/end. Record final positions before
    // generic row movement can prune pins, then restore them after rows settle.
    // Virtual placements follow text; relative placements follow their root.
    internal void BeginMarginScroll(TerminalScreen screen, int top, int bottom, int delta,
        uint cellWidth, uint cellHeight, bool windowShift = false, int left = 0, int right = int.MaxValue)
    {
        _marginScrollRestores.Clear();
        int activeTop = screen.GetAbsoluteRowForViewportRow(0);
        bool clippedAny = false;
        foreach ((PlacementKey key, Placement placement) in _placements)
        {
            if (placement.Anchor is not { } anchor || !screen.TryResolveAnchor(anchor, out var origin)) continue;
            int row = origin.Row - activeTop;
            // History keeps its tracked position. A window shift must restore
            // every active placement, including stationary margin straddlers.
            if (row < 0 || row >= screen.ViewportRows) continue;
            if (!windowShift && (row < top || row > bottom)) continue;
            int finalRow = row;
            if (delta != 0 && _images.TryGetValue(key.ImageId, out Image? image))
            {
                uint width = (uint)image.Animation.Width;
                uint height = (uint)image.Animation.Height;
                ManagedKittyPlacementGeometry grid = placement.Options.Calculate(width, height, cellWidth, cellHeight);
                if (grid.Rows > 0 && grid.Columns > 0 && row >= top && (long)row + grid.Rows - 1 <= bottom &&
                    origin.Column >= left && Math.Min((long)origin.Column + grid.Columns - 1, screen.Columns - 1) <= right)
                {
                    long moved = (long)row + delta;
                    uint topClip = (uint)Math.Max(0, top - moved);
                    uint bottomClip = (uint)Math.Max(0, moved + grid.Rows - 1 - bottom);
                    bool visible = topClip < grid.Rows && bottomClip < grid.Rows;
                    ManagedKittyPlacementOptions clipped = placement.Options;
                    if (visible && topClip > 0)
                        visible = placement.Options.TryClipTop(width, height, cellWidth, cellHeight, topClip, grid.Rows, out clipped);
                    else if (visible && bottomClip > 0)
                        visible = placement.Options.TryClipBottom(width, height, cellWidth, cellHeight, bottomClip, grid.Rows, out clipped);
                    if (!visible)
                    {
                        RemovePlacement(screen, key);
                        clippedAny = true;
                        continue;
                    }
                    finalRow = (int)Math.Max(top, moved);
                    if (topClip > 0 || bottomClip > 0)
                    {
                        placement.Options = clipped;
                        _generation++;
                        clippedAny = true;
                    }
                }
            }
            _marginScrollRestores.Add(new(anchor, origin.Column, finalRow));
        }
        if (clippedAny) RemoveOrphans(screen);
    }

    internal void EndMarginScroll(TerminalScreen screen)
    {
        foreach (MarginScrollRestore restore in _marginScrollRestores)
            screen.MoveAnchor(restore.Anchor, screen.GetAbsoluteRowForViewportRow(restore.Row), restore.Column);
        _marginScrollRestores.Clear();
    }

    private readonly record struct MarginScrollRestore(TerminalScreenAnchor Anchor, int Column, int Row);
}
