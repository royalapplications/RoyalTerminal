// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>An opaque, immutable identity for a screen-owned tracked cell.</summary>
internal sealed class TerminalScreenAnchor { }

public sealed partial class TerminalScreen
{
    // Do not attach identities to reusable TerminalRow objects. A state copy
    // shares the identities, but owns independent positions until publication.
    private Dictionary<TerminalScreenAnchor, TrackedCell> _trackedAnchors = [];
    private long _anchorRevision;

    /// <summary>Tracks a valid cell in the active buffer. Caller holds the screen lock.</summary>
    internal TerminalScreenAnchor CreateAnchor(int absoluteRow, int column)
    {
        ValidateAnchorPosition(absoluteRow, column);
        TerminalScreenAnchor token = new();
        _trackedAnchors.Add(token, new(absoluteRow, column, _alternateBufferActive));
        _anchorRevision++;
        return token;
    }

    /// <summary>Resolves a non-pruned cell in the active buffer.</summary>
    internal bool TryResolveAnchor(TerminalScreenAnchor token, out TerminalGridPosition position)
    {
        if (_trackedAnchors.TryGetValue(token, out TrackedCell cell) &&
            cell.Alternate == _alternateBufferActive && cell.Row >= 0)
        {
            position = new(Column: cell.Column, Row: cell.Row);
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>Moves an existing active-buffer anchor, including restoring a pruned pin.</summary>
    internal bool MoveAnchor(TerminalScreenAnchor token, int absoluteRow, int column)
    {
        ValidateAnchorPosition(absoluteRow, column);
        ref TrackedCell cell = ref CollectionsMarshal.GetValueRefOrNullRef(_trackedAnchors, token);
        if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref cell) || cell.Alternate != _alternateBufferActive)
        {
            return false;
        }

        cell = new(absoluteRow, column, cell.Alternate);
        _anchorRevision++;
        return true;
    }

    /// <summary>Releases an identity; a subsequent move cannot resurrect it.</summary>
    internal bool ReleaseAnchor(TerminalScreenAnchor token)
    {
        if (!_trackedAnchors.Remove(token)) return false;
        _anchorRevision++;
        return true;
    }

    /// <summary>Tracks an in-place vertical row copy, pruning cells scrolled outside its region.</summary>
    internal void ShiftAnchorsInViewportRows(int startViewportRow, int endViewportRow, int rowDelta,
        int startColumn = 0, int endColumn = int.MaxValue)
    {
        if (_trackedAnchors.Count == 0 || rowDelta == 0) return;
        _anchorRevision++;
        int start = GetAbsoluteRowForViewportRow(Math.Clamp(startViewportRow, 0, ViewportRows - 1));
        int end = GetAbsoluteRowForViewportRow(Math.Clamp(endViewportRow, 0, ViewportRows - 1));
        foreach ((TerminalScreenAnchor token, TrackedCell original) in _trackedAnchors)
        {
            if (original.Alternate != _alternateBufferActive || original.Row < start || original.Row > end) continue;
            if (original.Column < startColumn || original.Column > endColumn) continue;
            long row = (long)original.Row + rowDelta;
            ref TrackedCell cell = ref CollectionsMarshal.GetValueRefOrNullRef(_trackedAnchors, token);
            cell.Row = row < start || row > end ? -1 : (int)row;
        }
    }

    private void ValidateAnchorPosition(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, TotalRows);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
    }

    private void ShiftAnchorsAfterTopRowsRemoved(int count)
    {
        if (count <= 0) return;
        _anchorRevision++;
        foreach ((TerminalScreenAnchor token, TrackedCell original) in _trackedAnchors)
        {
            if (original.Alternate != _alternateBufferActive || original.Row < 0) continue;
            ref TrackedCell cell = ref CollectionsMarshal.GetValueRefOrNullRef(_trackedAnchors, token);
            cell.Row = Math.Max(-1, original.Row - count);
        }
    }

    private void PruneAnchorsFromRow(int firstRow, bool alternate)
    {
        _anchorRevision++;
        foreach ((TerminalScreenAnchor token, TrackedCell original) in _trackedAnchors)
        {
            if (original.Alternate != alternate || original.Row < firstRow) continue;
            CollectionsMarshal.GetValueRefOrNullRef(_trackedAnchors, token).Row = -1;
        }
    }

    private void ClampActiveAnchorsToColumns()
    {
        _anchorRevision++;
        foreach ((TerminalScreenAnchor token, TrackedCell original) in _trackedAnchors)
        {
            if (original.Alternate != _alternateBufferActive || original.Row < 0) continue;
            CollectionsMarshal.GetValueRefOrNullRef(_trackedAnchors, token).Column = Math.Min(original.Column, Columns - 1);
        }
    }

    private List<TerminalScreenAnchor>? AppendTrackedCellReflowAnchors(List<ReflowAnchorPosition> anchors)
    {
        List<TerminalScreenAnchor>? identities = null;
        foreach ((TerminalScreenAnchor token, TrackedCell cell) in _trackedAnchors)
        {
            if (cell.Alternate != _alternateBufferActive || cell.Row < 0) continue;
            (identities ??= []).Add(token);
            anchors.Add(new(cell.Row, cell.Column, cellAnchor: true));
        }
        return identities;
    }

    private void RemapTrackedCellAnchors(
        List<TerminalScreenAnchor> identities,
        List<ReflowAnchorPosition> positions,
        int offset,
        int removedRows)
    {
        _anchorRevision++;
        for (int i = 0; i < identities.Count; i++)
        {
            ReflowAnchorPosition mapped = positions[offset + i];
            ref TrackedCell cell = ref CollectionsMarshal.GetValueRefOrNullRef(_trackedAnchors, identities[i]);
            int row = (mapped.IsMapped ? mapped.NewAbsoluteRow : mapped.OldAbsoluteRow) - removedRows;
            cell.Row = row < 0 || row >= TotalRows ? -1 : row;
            cell.Column = Math.Clamp(mapped.NewColumn, 0, Columns - 1);
        }
    }

    private struct TrackedCell(int row, int column, bool alternate)
    {
        internal int Row = row;
        internal int Column = column;
        internal readonly bool Alternate = alternate;
    }

    private static TerminalGridPosition MapLogicalCellOffsetToReflowedPosition(
        ReadOnlySpan<TerminalCell> cells, int columns, int offset)
    {
        int row = 0;
        int column = 0;
        for (int index = 0; index < cells.Length;)
        {
            TerminalCell cell = cells[index];
            if (cell.Width == 0)
            {
                if (index == offset) return new(Math.Min(column, columns - 1), row);
                index++;
                continue;
            }

            int sourceStep = GetReflowSourceStep(cells, index);
            int width = cell.Width > 1 && columns > 1 ? 2 : 1;
            if (column + width > columns)
            {
                row++;
                column = 0;
            }

            // Cell anchors follow the next cell across a wrap, whereas cursor
            // offsets can intentionally refer to the previous row's right edge.
            if (offset < index + sourceStep)
                return new(column + Math.Min(offset - index, width - 1), row);

            column += width;
            index += sourceStep;
        }
        return new(Math.Min(column, columns - 1), row);
    }
}
