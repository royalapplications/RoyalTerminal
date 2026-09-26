// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    // One owner coordinates all per-page metadata through COW publication.
    private GhosttySnapshotPageTracker? _snapshotPageTracker;
    private GhosttySnapshotAllocation? _snapshotPageLayout;
    private int _snapshotPageAlignment;

    internal bool TracksSnapshotMetadata => _snapshotScrollbackQuota is not null || _snapshotRowGeometry;

    internal bool SnapshotMutationFailed => _snapshotPageTracker?.MutationFailed == true;
    internal void ThrowIfSnapshotMutationFailed() => _snapshotPageTracker?.ThrowIfMutationFailed();
    internal void RecordSnapshotMutationFailure(Exception failure)
        => (_snapshotPageTracker ??= new()).RecordMutationFailure(failure);

    internal byte TakeSnapshotCursorStyleDrops() => _snapshotPageTracker?.TakeCursorStyleDrops() ?? 0;

    internal void AcknowledgeSnapshotCursorStyle(int key) => _snapshotPageTracker?.AcknowledgeCursorStyle(key);

    internal bool SnapshotCursorStyleIsCurrent(int key, int cursorRow, GhosttySnapshotStyle pen)
    {
        if (!TracksSnapshotMetadata) return true;
        TerminalRowBuffer? rows = GetSnapshotRows(key);
        return rows is null || (uint)cursorRow >= (uint)ViewportRows || rows.Count < ViewportRows ||
            _snapshotPageTracker?.IsCurrent(key, rows[rows.Count - ViewportRows + cursorRow], pen) == true;
    }

    internal bool SnapshotStyleChanged(int key, int cursorRow, GhosttySnapshotStyle previous, GhosttySnapshotStyle current)
    {
        uint counter = 0;
        return SnapshotStyleChanged(key, cursorRow, previous, current, ref counter);
    }

    internal bool SnapshotStyleChanged(int key, int cursorRow, GhosttySnapshotStyle previous, GhosttySnapshotStyle current, ref uint hyperlinkCounter)
    {
        if (!TracksSnapshotMetadata) return true;
        TerminalRowBuffer? rows = GetSnapshotRows(key);
        if (rows is null || (uint)cursorRow >= (uint)ViewportRows || rows.Count < ViewportRows) return true;
        int index = rows.Count - ViewportRows + cursorRow;
        TerminalRow row = rows[index];
        if (_snapshotPageTracker?.IsCurrent(key, row, current) == true) return true;
        GhosttySnapshotAllocation layout = SnapshotPageLayout();
        GhosttySnapshotPageTracker tracker = _snapshotPageTracker ??= new();
        if (row.SnapshotAllocation is null && !tracker.AssignTailRow(rows, index, layout))
            _ = GhosttySnapshotLiveAllocation.Measure(this, rows, layout);
        return tracker.IsCurrent(key, row, current) || tracker.ChangeCursor(rows, key, row, previous, current, layout, this, ref hyperlinkCounter);
    }

    internal GhosttySnapshotPageTracker.RowEdit EditSnapshotRowMetadata(TerminalRow row)
    {
        if (!TracksSnapshotMetadata) return default;
        GhosttySnapshotAllocation layout = SnapshotPageLayout();
        GhosttySnapshotPageTracker tracker = _snapshotPageTracker ??= new();
        if (row.SnapshotAllocation is null &&
            !(_rows.Count > 0 && ReferenceEquals(_rows[_rows.Count - 1], row) && tracker.AssignTailRow(_rows, _rows.Count - 1, layout)))
            _ = GhosttySnapshotLiveAllocation.Measure(this, _rows, layout);
        return tracker.EditRow(_rows, row, layout, this);
    }

    internal byte[]? SnapshotHyperlinkEncoding(int token)
    {
        if (token == 0) return null;
        if (TryGetHyperlink(token, out TerminalHyperlink? link) && link is not null) return link.SnapshotEncoding;
        // The legacy URL-only API has no OSC identity. Encode its stable host
        // token as the same synthetic implicit ID used by snapshot export.
        return TryGetHyperlinkUrl(token, out string? uri) && uri is not null
            ? new TerminalHyperlink(Encoding.UTF8.GetBytes(uri), default, unchecked((uint)token)).SnapshotEncoding : null;
    }

    internal int SnapshotHyperlinkChanged(int key, int cursorRow, GhosttySnapshotStyle pen,
        int token, ref uint counter, bool restart = false)
    {
        if (!TracksSnapshotMetadata) return token;
        SnapshotStyleChanged(key, cursorRow, pen, pen, ref counter);
        TerminalRowBuffer? rows = GetSnapshotRows(key);
        if (rows is null || (uint)cursorRow >= (uint)ViewportRows || rows.Count < ViewportRows) return token;
        return _snapshotPageTracker!.ChangeHyperlink(this, rows, key, rows[rows.Count - ViewportRows + cursorRow],
            token, ref counter, restart, SnapshotPageLayout());
    }

    internal void EndSnapshotCursorHyperlink(int key) => _snapshotPageTracker?.EndCursorHyperlink(key);

    internal int SnapshotCursorHyperlinkToken(int key, int fallback)
        => TracksSnapshotMetadata ? _snapshotPageTracker?.CursorHyperlinkToken(key, fallback) ?? fallback : fallback;

    internal GhosttySnapshotPageTracker.CursorResizeLease? BeginSnapshotCursorResize(int key, int cursorRow,
        GhosttySnapshotStyle pen, ref int hyperlinkToken, ref uint counter)
    {
        if (!TracksSnapshotMetadata) return null;
        hyperlinkToken = SnapshotHyperlinkChanged(key, cursorRow, pen, hyperlinkToken, ref counter);
        TerminalRowBuffer? rows = GetSnapshotRows(key);
        if (rows is null || (uint)cursorRow >= (uint)ViewportRows || rows.Count < ViewportRows) return null;
        return _snapshotPageTracker!.BeginCursorResize(this, rows, key, rows[rows.Count - ViewportRows + cursorRow],
            ref hyperlinkToken, SnapshotPageLayout());
    }

    internal int RestoreSnapshotResizeCursor(int key, int cursorRow, GhosttySnapshotStyle pen, int token, ref uint counter)
    {
        if (!SnapshotStyleChanged(key, cursorRow, default, pen, ref counter)) pen = default;
        // Screen.resize restarts even on a surviving page. Unlike ordinary
        // same-page motion, that consumes a new implicit identity each time.
        TerminalHyperlink? link = null;
        bool implicitId = token != 0 && TryGetHyperlink(token, out link) && link is { IsExplicit: false };
        if (implicitId)
        {
            token = RegisterHyperlink(link!.UriBytes, default, counter);
        }
        token = SnapshotHyperlinkChanged(key, cursorRow, pen, token, ref counter, restart: true);
        if (implicitId && token != 0) counter = unchecked(counter + 1);
        return token;
    }

    private GhosttySnapshotAllocation SnapshotPageLayout()
    {
        int alignment = _snapshotScrollbackQuota?.PageAlignment ??
            (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16384 : 4096);
        if (_snapshotPageLayout is null || _snapshotPageAlignment != alignment)
        {
            _snapshotPageAlignment = alignment;
            _snapshotPageLayout = new(alignment);
        }
        return _snapshotPageLayout;
    }

    internal GhosttySnapshotPageAllocation SnapshotAllocationReplaced(GhosttySnapshotPageAllocation previous, GhosttySnapshotPageAllocation replacement)
        => _snapshotPageTracker?.AllocationReplaced(previous, replacement) ?? replacement;

    internal void SnapshotRowsObserved(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows)
        => _snapshotPageTracker?.ObserveRowSlots(page, rows);

    internal bool TryGetSnapshotStyleUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows, out int count)
    {
        count = 0;
        return _snapshotPageTracker?.TryGetStyleUsage(page, rows, out count) == true;
    }

    internal bool TryGetSnapshotGraphemeUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows,
        out ulong cells, out ulong bytes)
    {
        cells = bytes = 0;
        return _snapshotPageTracker?.TryGetGraphemeUsage(page, rows, out cells, out bytes) == true;
    }

    internal bool TryGetSnapshotHyperlinkUsage(GhosttySnapshotPageAllocation page, IReadOnlyList<TerminalRow> rows,
        out ulong links, out ulong cells, out ulong bytes)
    {
        links = cells = bytes = 0;
        return _snapshotPageTracker?.TryGetHyperlinkUsage(page, rows, out links, out cells, out bytes) == true;
    }

    private void RetireSnapshotRows(int start, int count)
    {
        if (!TracksSnapshotMetadata || count == 0) return;
        (_snapshotPageTracker ??= new()).RetireRows(_rows, start, count);
    }

    private void RemoveRows(int start, int count)
    {
        RetireSnapshotRows(start, count);
        _rows.RemoveRange(start, count);
    }

    private void ClearRow(TerminalRow row)
    {
        using GhosttySnapshotPageTracker.RowEdit styles = row.SnapshotAllocation is null ? default : EditSnapshotRowMetadata(row);
        styles.Clear(0, row.PreservedColumns);
        row.Clear(DefaultForeground, DefaultBackground);
    }
}
