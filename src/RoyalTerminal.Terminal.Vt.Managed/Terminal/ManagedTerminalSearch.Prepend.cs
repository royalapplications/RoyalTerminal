// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

internal sealed partial class ManagedTerminalSearch
{
    // Ghostty 06178eeaa resumes a completed search when older snapshot pages
    // arrive. Our forward KMP scanner must also reconcile the seam's deferred
    // whitespace and overlapping matches before reusing its immutable tail.
    private sealed class PrependReuse(int rows, List<TerminalSearchMatch> results, List<Checkpoint> checkpoints)
    {
        internal readonly int Rows = rows;
        internal readonly List<TerminalSearchMatch> Results = results;
        internal readonly List<Checkpoint> Checkpoints = checkpoints;
        internal int NextCheckpoint;
    }

    private PrependReuse? PreparePrependReuse(ManagedSearchSnapshot snapshot, int unchanged, CancellationToken cancellation)
    {
        if (!_complete || _snapshot is null || unchanged != 0 || _checkpoints.Count == 0) return null;
        int added = snapshot.PrependedRows(_snapshot, cancellation);
        if (added == 0) return null;
        // Prepare every new owner before detaching the old cache. During a
        // cancelled/failed scan only the normal completed prefix is resumable;
        // these old lists are local, never retained by a partial publication.
        PrependReuse reuse = new(added, _results, _checkpoints);
        List<TerminalSearchMatch> results = new(_results.Count);
        List<Checkpoint> checkpoints = new(_checkpoints.Count);
        _results = results;
        _checkpoints = checkpoints;
        return reuse;
    }

    private bool ReusePrependedTail(PrependReuse reuse, int row, long blankCells, int blankRows,
        CellPoint lastPoint, CancellationToken cancellation)
    {
        while (reuse.NextCheckpoint < reuse.Checkpoints.Count &&
            reuse.Checkpoints[reuse.NextCheckpoint].Row + reuse.Rows < row) reuse.NextCheckpoint++;
        if (reuse.NextCheckpoint == reuse.Checkpoints.Count) return false;
        Checkpoint checkpoint = reuse.Checkpoints[reuse.NextCheckpoint];
        if (checkpoint.Row + reuse.Rows != row || checkpoint.BlankCells != blankCells ||
            checkpoint.BlankRows != blankRows || Shift(checkpoint.LastPoint, reuse.Rows) != lastPoint ||
            (checkpoint.Prefix?.Length ?? 0) != _matched) return false;
        int first = (_nextPoint - _matched + _needle.Length) % _needle.Length;
        for (int i = 0; i < _matched; i++)
            if (_points[(first + i) % _needle.Length] != Shift(checkpoint.Prefix![i], reuse.Rows)) return false;

        // Equal KMP state, prefix coordinates and deferred formatting state
        // over an identity-equal suffix prove every later match is unchanged.
        // This includes cross-checkpoint matches and the final synthetic LF.
        int resultOffset = _results.Count - checkpoint.Results;
        for (int i = checkpoint.Results; i < reuse.Results.Count; i++)
        {
            if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
            TerminalSearchMatch match = reuse.Results[i];
            _results.Add(match with
            {
                AbsoluteRow = match.AbsoluteRow + reuse.Rows,
                EndAbsoluteRow = match.EndAbsoluteRow + reuse.Rows,
            });
        }
        for (int i = reuse.NextCheckpoint; i < reuse.Checkpoints.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            Checkpoint old = reuse.Checkpoints[i];
            CellPoint[]? prefix = old.Prefix is null ? null : new CellPoint[old.Prefix.Length];
            if (prefix is not null)
                for (int point = 0; point < prefix.Length; point++) prefix[point] = Shift(old.Prefix![point], reuse.Rows);
            _checkpoints.Add(old with
            {
                Row = old.Row + reuse.Rows,
                Results = old.Results + resultOffset,
                LastPoint = Shift(old.LastPoint, reuse.Rows),
                Prefix = prefix,
            });
        }
        cancellation.ThrowIfCancellationRequested();
        return true;
    }

    private static CellPoint Shift(CellPoint point, int rows) => new(point.Row + rows, point.Column);
}
