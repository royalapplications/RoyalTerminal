// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Frozen;

namespace RoyalTerminal.Avalonia.Rendering;

internal readonly record struct TerminalKittyPlacementKey(uint ImageId, uint PlacementId, bool Internal);

internal readonly record struct TerminalKittyPlaceholderTarget(
    TerminalKittyPlacementKey Key, bool Virtual, uint Columns, uint Rows);

internal readonly record struct TerminalKittyRelativePlacement(
    TerminalKittyPlacementKey Root, long ColumnOffset, long RowOffset,
    uint Columns, uint Rows, TerminalKittyImagePlacement Geometry);

internal readonly record struct TerminalKittyLocatedPlaceholder(int Row, TerminalKittyPlaceholderRun Run);

/// <summary>Immutable placement lookup and geometry, safe to share across held-screen copies.</summary>
internal sealed class TerminalKittyPlaceholderScene
{
    private readonly FrozenDictionary<TerminalKittyPlacementKey, TerminalKittyPlaceholderTarget> _targets;
    private readonly FrozenDictionary<uint, TerminalKittyPlaceholderTarget> _defaults;
    internal readonly TerminalKittyRelativePlacement[] Relatives;
    internal readonly TerminalKittyImagePlacement[] Fixed;
    internal readonly uint CellWidth;
    internal readonly uint CellHeight;

    internal TerminalKittyPlaceholderScene(IReadOnlyList<TerminalKittyPlaceholderTarget> targets,
        IReadOnlyList<TerminalKittyRelativePlacement> relatives, TerminalKittyImagePlacement[] fixedPlacements,
        uint cellWidth, uint cellHeight)
    {
        Dictionary<TerminalKittyPlacementKey, TerminalKittyPlaceholderTarget> all = new(targets.Count);
        Dictionary<uint, TerminalKittyPlaceholderTarget> defaults = [];
        foreach (TerminalKittyPlaceholderTarget target in targets)
        {
            all[target.Key] = target;
            if (!target.Virtual) continue;
            if (!defaults.TryGetValue(target.Key.ImageId, out TerminalKittyPlaceholderTarget previous) ||
                previous.Key.Internal && !target.Key.Internal ||
                previous.Key.Internal == target.Key.Internal && target.Key.PlacementId < previous.Key.PlacementId)
                defaults[target.Key.ImageId] = target;
        }
        _targets = all.ToFrozenDictionary();
        _defaults = defaults.ToFrozenDictionary();
        Relatives = new TerminalKittyRelativePlacement[relatives.Count];
        for (int i = 0; i < relatives.Count; i++) Relatives[i] = relatives[i];
        Fixed = fixedPlacements;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
    }

    internal bool TryGetTarget(in TerminalKittyPlaceholderRun run, out TerminalKittyPlaceholderTarget target)
        => run.PlacementId == 0
            ? _defaults.TryGetValue(run.ImageId, out target)
            : _targets.TryGetValue(new(run.ImageId, run.PlacementId, false), out target);

    internal bool Matches(IReadOnlyList<TerminalKittyPlaceholderTarget> targets,
        IReadOnlyList<TerminalKittyRelativePlacement> relatives, uint cellWidth, uint cellHeight)
    {
        if (_targets.Count != targets.Count || Relatives.Length != relatives.Count || CellWidth != cellWidth || CellHeight != cellHeight)
            return false;
        for (int i = 0; i < targets.Count; i++)
            if (!_targets.TryGetValue(targets[i].Key, out var previous) || previous != targets[i]) return false;
        for (int i = 0; i < relatives.Count; i++)
        {
            TerminalKittyRelativePlacement left = Relatives[i], right = relatives[i];
            if (left.Root != right.Root || left.ColumnOffset != right.ColumnOffset || left.RowOffset != right.RowOffset ||
                left.Columns != right.Columns || left.Rows != right.Rows ||
                !TerminalKittyImagePlacement.GeometryEquals(left.Geometry, right.Geometry)) return false;
        }
        return true;
    }
}

public sealed partial class TerminalScreen
{
    private TerminalKittyPlaceholderScene? _kittyPlaceholderScene;
    private TerminalKittyLocatedPlaceholder[]? _kittyPlaceholderRuns;

    internal ReadOnlySpan<TerminalKittyImagePlacement> GetFixedKittyPlacements()
        => _kittyPlaceholderScene?.Fixed ?? _kittyPlacements;

    internal bool MatchesKittyPlaceholderScene(IReadOnlyList<TerminalKittyPlaceholderTarget> targets,
        IReadOnlyList<TerminalKittyRelativePlacement> relatives, uint cellWidth, uint cellHeight)
    {
        bool active = relatives.Count > 0;
        for (int i = 0; !active && i < targets.Count; i++) active = targets[i].Virtual;
        return !active ? _kittyPlaceholderScene is null
            : _kittyPlaceholderScene?.Matches(targets, relatives, cellWidth, cellHeight) == true;
    }

    internal void SetKittyPlaceholderScene(IReadOnlyList<TerminalKittyPlaceholderTarget> targets,
        IReadOnlyList<TerminalKittyRelativePlacement> relatives, uint cellWidth, uint cellHeight)
    {
        bool active = relatives.Count > 0;
        for (int i = 0; !active && i < targets.Count; i++) active = targets[i].Virtual;
        if (!active) return;
        _kittyPlaceholderScene = new(targets, relatives,
            _kittyAnchoredPlacements is null ? _kittyPlacements : [], cellWidth, cellHeight);
        _kittyPlaceholderRuns = null;
        _kittyProjectionState = null;
    }

    // Scan each frame like Ghostty, but retain the immutable run array when unchanged.
    // Exact run comparison avoids a hash collision making stale placements permanent.
    private bool RefreshPlaceholderRuns()
    {
        TerminalKittyLocatedPlaceholder[] old = _kittyPlaceholderRuns ?? [];
        List<TerminalKittyLocatedPlaceholder>? changed = null;
        int count = 0;
        for (int row = 0; row < ViewportRows; row++)
        {
            TerminalKittyPlaceholderScanner scanner = new(GetViewportRow(row).ReadOnlyCells);
            while (scanner.TryReadNext(out TerminalKittyPlaceholderRun run))
            {
                TerminalKittyLocatedPlaceholder located = new(row, run);
                if (changed is null && (count >= old.Length || old[count] != located))
                {
                    changed = new(Math.Max(old.Length, count + 1));
                    for (int i = 0; i < count; i++) changed.Add(old[i]);
                }
                changed?.Add(located);
                count++;
            }
        }
        if (changed is not null) _kittyPlaceholderRuns = changed.ToArray();
        else if (count != old.Length) _kittyPlaceholderRuns = old.AsSpan(0, count).ToArray();
        else if (_kittyPlaceholderRuns is not null) return false;
        else _kittyPlaceholderRuns = [];
        return true;
    }

    private void AppendPlaceholderProjection(List<TerminalKittyImagePlacement> visible, TerminalKittyPlaceholderScene scene)
    {
        Dictionary<TerminalKittyPlacementKey, TerminalGridPosition>? origins = scene.Relatives.Length > 0 ? [] : null;
        foreach (TerminalKittyLocatedPlaceholder located in _kittyPlaceholderRuns ?? [])
        {
            TerminalKittyPlaceholderRun run = located.Run;
            if (!scene.TryGetTarget(run, out TerminalKittyPlaceholderTarget target)) continue;
            // Fold origins even when the fragment falls entirely in letterbox padding.
            // Upstream uses independent min-x/min-y across visible runs for each exact key.
            if (origins is not null)
            {
                TerminalGridPosition origin = new(run.ScreenColumn, located.Row);
                if (origins.TryGetValue(target.Key, out TerminalGridPosition prior))
                    origin = new(Math.Min(origin.Column, prior.Column), Math.Min(origin.Row, prior.Row));
                origins[target.Key] = origin;
            }
            if (!TryGetKittyImageSource(unchecked((int)run.ImageId), out TerminalKittyImageSource? image) || image is null ||
                !TerminalKittyPlaceholderGeometry.TryProject(run, (uint)image.WidthPx, (uint)image.HeightPx,
                    target.Columns, target.Rows, scene.CellWidth, scene.CellHeight, out var geometry)) continue;
            visible.Add(new(image.ImageId, TerminalKittyImageLayer.BelowText, run.ScreenColumn, located.Row,
                SaturateKitty(geometry.OffsetX), SaturateKitty(geometry.OffsetY), SaturateKitty(geometry.Width), SaturateKitty(geometry.Height),
                SaturateKitty(geometry.SourceX), SaturateKitty(geometry.SourceY), SaturateKitty(geometry.SourceWidth), SaturateKitty(geometry.SourceHeight),
                SaturateKitty(scene.CellWidth), SaturateKitty(scene.CellHeight), TerminalKittyImagePlacementScaleMode.ColumnsAndRows, -1));
        }
        if (origins is null) return;
        foreach (TerminalKittyRelativePlacement relative in scene.Relatives)
        {
            if (!origins.TryGetValue(relative.Root, out TerminalGridPosition origin)) continue;
            AppendKittyProjection(visible, origin.Column + relative.ColumnOffset, origin.Row + relative.RowOffset,
                relative.Columns, relative.Rows, relative.Geometry);
        }
    }

    private static int SaturateKitty(uint value) => (int)Math.Min(value, int.MaxValue);
}
