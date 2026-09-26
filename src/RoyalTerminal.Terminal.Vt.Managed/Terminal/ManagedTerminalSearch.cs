// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Literal, ASCII-case-insensitive search over published terminal cells. KMP and
/// a coordinate ring retain only needle-sized scratch, even for long wrapped lines.
/// Immutable COW rows and restart checkpoints retain completed history work.
/// The scanner is owned by one caller/worker; capture requires the screen lock.
/// </summary>
internal sealed class ManagedTerminalSearch
{
    private string _needle = string.Empty;
    private int[] _failure = [];
    private CellPoint[] _points = [];
    private int _matched;
    private int _nextPoint;
    private ManagedSearchSnapshot? _snapshot;
    private readonly List<TerminalSearchMatch> _results = [];
    private readonly List<Checkpoint> _checkpoints = [];
    private int _scannedRows;
    private bool _complete;
    private CancellationToken _cancellation;
    private int _feedCount;

    internal int RowsScannedLastSearch { get; private set; }

    private readonly record struct CellPoint(int Row, int Column);
    private readonly record struct Checkpoint(int Row, int Results, long BlankCells, int BlankRows,
        CellPoint LastPoint, CellPoint[]? Prefix = null);

    internal void Populate(TerminalScreen screen, string needle, List<TerminalSearchMatch> destination)
        => Populate(ManagedSearchSnapshot.Capture(screen, _snapshot), needle, destination);

    internal void Populate(ManagedSearchSnapshot snapshot, string needle,
        List<TerminalSearchMatch> destination, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        RowsScannedLastSearch = 0;
        if (!string.Equals(_needle, needle, StringComparison.Ordinal)) Reset();
        SetNeedle(needle ?? string.Empty);
        if (_needle.Length == 0) return;

        if (ReferenceEquals(snapshot, _snapshot) && _complete)
        {
            destination.AddRange(_results);
            return;
        }
        int unchanged = Math.Min(snapshot.CommonPrefix(_snapshot), _scannedRows);
        int checkpointIndex = _checkpoints.Count - 1;
        while (checkpointIndex >= 0 && _checkpoints[checkpointIndex].Row > unchanged) checkpointIndex--;
        Checkpoint checkpoint = checkpointIndex >= 0 ? _checkpoints[checkpointIndex] : default;
        if (checkpointIndex + 1 < _checkpoints.Count)
            _checkpoints.RemoveRange(checkpointIndex + 1, _checkpoints.Count - checkpointIndex - 1);
        if (checkpoint.Results < _results.Count) _results.RemoveRange(checkpoint.Results, _results.Count - checkpoint.Results);
        _snapshot = snapshot;
        _complete = false;
        _cancellation = cancellation;
        _feedCount = 0;
        _matched = checkpoint.Prefix?.Length ?? 0;
        _nextPoint = _matched;
        checkpoint.Prefix?.CopyTo(_points, 0);

        // Mirror plain, trimmed, unwrapped formatting without constructing text
        // for a row/page/buffer. Delay blanks so trailing spaces are not searchable.
        long blankCells = checkpoint.BlankCells;
        int blankRows = checkpoint.BlankRows;
        CellPoint lastPoint = checkpoint.LastPoint;
        int lastCheckpointRow = checkpoint.Row;
        Span<char> scalar = stackalloc char[2];
        for (int rowIndex = checkpoint.Row; rowIndex < snapshot.Rows.Length; rowIndex++)
        {
            _scannedRows = rowIndex;
            cancellation.ThrowIfCancellationRequested();
            // Preserve only the live KMP prefix, not a whole needle-sized ring.
            // Bound checkpoint memory for huge needles with long self-overlaps.
            if (_matched <= 256 && rowIndex - lastCheckpointRow >= 64)
            {
                CellPoint[]? prefix = null;
                if (_matched > 0)
                {
                    prefix = new CellPoint[_matched];
                    int first = (_nextPoint - _matched + _needle.Length) % _needle.Length;
                    for (int i = 0; i < prefix.Length; i++) prefix[i] = _points[(first + i) % _needle.Length];
                }
                _checkpoints.Add(new(rowIndex, _results.Count, blankCells, blankRows, lastPoint, prefix));
                lastCheckpointRow = rowIndex;
            }
            RowsScannedLastSearch++;
            TerminalRow row = snapshot.Rows[rowIndex];
            ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
            bool hasText = false;
            foreach (ref readonly TerminalCell cell in cells)
            {
                if (cell.Width != 0 && cell.HasContent) { hasText = true; break; }
            }

            if (!hasText)
            {
                blankRows++;
                continue;
            }

            for (int i = 0; i < blankRows; i++)
            {
                CellPoint point = i == 0 ? lastPoint : new(lastPoint.Row + i, 0);
                Feed('\n', point, _results);
            }
            blankRows = row.WrapsToNext ? 0 : 1;
            if (!row.IsWrapContinuation) blankCells = 0;

            for (int column = 0; column < cells.Length; column++)
            {
                ref readonly TerminalCell cell = ref cells[column];
                if (cell.Width == 0 || cell.IsWideSpacerHead) continue;
                if (!cell.HasContent || cell.Codepoint == ' ')
                {
                    blankCells++;
                    continue;
                }

                // The plain formatter maps deferred blanks backwards from the
                // next visible cell, including blanks in a preceding wrapped row.
                long firstBlank = (long)rowIndex * snapshot.Columns + column - blankCells;
                for (long offset = 0; offset < blankCells; offset++)
                {
                    long position = Math.Max(0, firstBlank + offset);
                    Feed(' ', new((int)(position / snapshot.Columns), (int)(position % snapshot.Columns)), _results);
                }
                blankCells = 0;
                lastPoint = new(rowIndex, column);
                if (!string.IsNullOrEmpty(cell.Grapheme))
                {
                    foreach (char character in cell.Grapheme) Feed(character, lastPoint, _results);
                }
                else if ((uint)cell.Codepoint < 0x80)
                {
                    Feed((char)cell.Codepoint, lastPoint, _results);
                }
                else
                {
                    Rune rune = Rune.TryCreate(cell.Codepoint, out Rune valid) ? valid : Rune.ReplacementChar;
                    int length = rune.EncodeToUtf16(scalar);
                    for (int i = 0; i < length; i++) Feed(scalar[i], lastPoint, _results);
                }
            }
        }

        _scannedRows = snapshot.Rows.Length;
        if (snapshot.Rows.Length > 0 && !snapshot.Rows[^1].WrapsToNext)
            Feed('\n', lastPoint, _results);
        cancellation.ThrowIfCancellationRequested();
        _complete = true;
        destination.AddRange(_results);
    }

    internal void Reset()
    {
        _snapshot = null;
        _results.Clear();
        _checkpoints.Clear();
        _scannedRows = 0;
        _complete = false;
    }

    private void SetNeedle(string needle)
    {
        if (string.Equals(_needle, needle, StringComparison.Ordinal)) return;
        _needle = needle;
        if (_failure.Length < needle.Length)
        {
            _failure = new int[needle.Length];
            _points = new CellPoint[needle.Length];
        }
        if (needle.Length == 0) return;
        _failure[0] = 0;
        for (int i = 1, prefix = 0; i < needle.Length; i++)
        {
            char character = Fold(needle[i]);
            while (prefix > 0 && Fold(needle[prefix]) != character) prefix = _failure[prefix - 1];
            if (Fold(needle[prefix]) == character) prefix++;
            _failure[i] = prefix;
        }
    }

    private void Feed(char character, CellPoint point, List<TerminalSearchMatch> destination)
    {
        if ((++_feedCount & 1023) == 0) _cancellation.ThrowIfCancellationRequested();
        character = Fold(character);
        _points[_nextPoint] = point;
        if (++_nextPoint == _needle.Length) _nextPoint = 0;
        while (_matched > 0 && Fold(_needle[_matched]) != character) _matched = _failure[_matched - 1];
        if (Fold(_needle[_matched]) == character) _matched++;
        if (_matched != _needle.Length) return;

        CellPoint start = _points[_nextPoint];
        destination.Add(new(start.Row, start.Column, point.Column) { EndAbsoluteRow = point.Row });
        // Preserve the longest suffix so overlapping matches are returned.
        _matched = _failure[_matched - 1];
    }

    private static char Fold(char value) => value is >= 'A' and <= 'Z' ? (char)(value + ('a' - 'A')) : value;
}
