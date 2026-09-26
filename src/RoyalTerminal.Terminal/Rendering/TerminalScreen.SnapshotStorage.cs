// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Theming;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    private GhosttySnapshotScrollbackQuota? _snapshotScrollbackQuota;

    /// <summary>
    /// Logical page quota for live scrollback and incremental-history admission.
    /// Changing this immediately evicts eligible whole historical pages in both
    /// buffers, never an active-boundary page. Null disables logical quotas.
    /// Raising a limit does not reopen a previously dropped history gap.
    /// Serialize changes with screen/processor access, as for ScrollbackLimit.
    /// </summary>
    public GhosttySnapshotScrollbackQuota? SnapshotScrollbackQuota
    {
        get => _snapshotScrollbackQuota;
        set { value?.Validate(); _snapshotScrollbackQuota = value; EnforceSnapshotQuotaChange(); }
    }

    internal bool FitsSnapshotHistoryQuota(int key, GhosttySnapshotPage page)
    {
        if (_snapshotScrollbackQuota is not { } quota) return true;
        TerminalRowBuffer? rows = GetSnapshotRows(key);
        if (rows is null || Columns is < 1 or > ushort.MaxValue || ViewportRows is < 1 or > ushort.MaxValue) return false;
        GhosttySnapshotAllocation allocation = new(quota.PageAlignment);
        ulong bytes = quota.MaximumBytes.HasValue
            ? GhosttySnapshotLiveAllocation.Add(GhosttySnapshotLiveAllocation.Measure(this, rows, allocation), allocation.AllocatedBytes(page.Capacity))
            : 0;
        if (bytes == ulong.MaxValue) return false; // Unrepresentable live capacity must not fit an explicit ulong.MaxValue budget.
        return allocation.Fits(Columns, ViewportRows, bytes, (ulong)rows.Count + (ulong)page.Grid.Rows,
            quota.MaximumBytes, quota.MaximumRows);
    }

    private GhosttySnapshotReflowAllocation? CreateSnapshotReflowAllocation(int columns)
    {
        GhosttySnapshotPageTracker? tracker = PrepareSnapshotResize(columns);
        return tracker is null ? null : new(_rows, columns, SnapshotPageLayout(), tracker, this);
    }

    private GhosttySnapshotPageTracker? PrepareSnapshotResize(int columns)
    {
        if (_rows.Count == 0 || columns is < 1 or > ushort.MaxValue ||
            _snapshotScrollbackQuota is null && _rows[0].SnapshotAllocation is null) return null;
        GhosttySnapshotAllocation layout = SnapshotPageLayout();
        for (int i = 0; i < _rows.Count; i++)
            if (_rows[i].PreservedColumns is < 1 or > ushort.MaxValue) return null;
        GhosttySnapshotPageTracker tracker = _snapshotPageTracker ??= new();
        bool allAccounted = true;
        for (int i = 0; i < _rows.Count; i++) allAccounted &= _rows[i].SnapshotAllocation is not null;
        // Existing PAGE seeds are more precise than visible styles (notably
        // inline background cells). Reconcile them before the generic metadata
        // checkpoint; otherwise that estimate can invent a style allocation.
        if (allAccounted) _ = tracker.ReflowSources(_rows, layout, this);
        // Observe live content before reflow replaces its rows. A metadata
        // overflow may become representable after reflow splits the content;
        // retain its existing page identity instead of dropping all accounting.
        _ = GhosttySnapshotLiveAllocation.Measure(this, _rows, layout);
        return tracker;
    }
    // Allocate a lineage only when a decoder tracks this terminal. COW publication
    // preserves it; a separately restored terminal receives a different identity.
    private object? _snapshotLineage;
    private ulong _snapshotAlternateGeneration;
    private bool _snapshotRowGeometry;

    internal object SnapshotLineage => _snapshotLineage ??= new object();

    internal ulong GetSnapshotGeneration(int key) => key switch
    {
        0 => 0, // Ghostty resets primary contents in place, retaining its ScreenSet slot.
        1 => _snapshotAlternateGeneration,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>
    /// Creates unpublished storage with no throwaway viewport. The snapshot adapter
    /// must install both row sets before handing this object to any live consumer.
    /// </summary>
    internal static TerminalScreen CreateSnapshotStorage(int columns, int rows, int scrollbackLimit, TerminalTheme theme)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(scrollbackLimit);
        ArgumentNullException.ThrowIfNull(theme);
        return new(new TerminalRowBuffer())
        {
            Columns = columns, ViewportRows = rows, _scrollbackLimit = scrollbackLimit,
            _theme = theme, DefaultForeground = theme.DefaultForeground, DefaultBackground = theme.DefaultBackground,
        };
    }

    /// <summary>
    /// Takes ownership of decoded rows in a fresh staging screen. Do not resize,
    /// trim incidental history, or execute a buffer switch: physical widths are
    /// snapshot state, even when different from the terminal's current columns.
    /// </summary>
    internal void InstallSnapshotRows(TerminalRow[] primary, TerminalRow[]? alternate, int activeKey)
    {
        if (_rows.Count != 0 || _primaryRows is not null || _alternateRows is not null)
            throw new InvalidOperationException("Snapshot rows require unpublished empty storage.");
        if (activeKey is < 0 or > 1 || activeKey == 1 && alternate is null)
            throw new InvalidDataException("Missing active snapshot screen.");
        ValidateRows(primary);
        if (alternate is not null) ValidateRows(alternate);
        TerminalRowBuffer primaryBuffer = ToBuffer(primary);
        TerminalRowBuffer? alternateBuffer = alternate is null ? null : ToBuffer(alternate);
        // No allocations after this point. Row and registry ownership stay together.
        _alternateBufferActive = activeKey == 1;
        _primaryRows = _alternateBufferActive ? primaryBuffer : null;
        _alternateRows = alternateBuffer;
        _rows = _alternateBufferActive ? alternateBuffer! : primaryBuffer;
        _snapshotRowGeometry = true;

        void ValidateRows(TerminalRow[] rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            if (rows.Length < ViewportRows) throw new InvalidDataException("Snapshot rows do not cover the viewport.");
            foreach (TerminalRow row in rows)
                if (row is null || row.Columns < 1) throw new InvalidDataException("Invalid snapshot row.");
        }

        static TerminalRowBuffer ToBuffer(TerminalRow[] rows)
        {
            TerminalRowBuffer buffer = new(rows.Length);
            foreach (TerminalRow row in rows) buffer.Add(row);
            return buffer;
        }
    }

    /// <summary>Accesses either buffer without a stateful switch; the caller holds the screen lock.</summary>
    internal TerminalRowBuffer? GetSnapshotRows(int key) => key switch
    {
        0 => _alternateBufferActive ? _primaryRows : _rows,
        1 => _alternateBufferActive ? _rows : _alternateRows,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>
    /// Atomically prepends one validated page. Caller holds the screen lock and
    /// has already checked generation, width and byte/row budgets. A missing
    /// buffer is an error, not an instruction to create one. This storage primitive
    /// does not itself decide whether live changes have invalidated the sequence.
    /// </summary>
    internal int PrependSnapshotHistory(int key, GhosttySnapshotPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        TerminalRowBuffer rows = GetSnapshotRows(key) ?? throw new InvalidOperationException("Snapshot screen no longer exists.");
        int added = page.Grid.Rows;
        _ = checked(rows.Count + added);
        bool alternate = key == 1;
        bool active = alternate == _alternateBufferActive;

        // PAGE decoding registers links. Isolate only those registries, not every
        // existing row/cell; a decode/allocation failure must leave live state intact.
        TerminalScreen owner = this;
        if (page.HyperlinkCount != 0)
        {
            owner = CreateSnapshotStorage(Columns, ViewportRows, _scrollbackLimit, _theme);
            owner._nextHyperlinkId = _nextHyperlinkId;
            CopyRegistry(_hyperlinksById, owner._hyperlinksById);
            CopyRegistry(_hyperlinkIdsByUrl, owner._hyperlinkIdsByUrl);
            owner._hyperlinkIdentities.CopyFrom(_hyperlinkIdentities);
        }
        TerminalRow[] decoded = GhosttySnapshotLivePage.Decode(page, owner);

        List<TerminalRasterImagePlacement>? placements = active ? _rasterPlacements
            : alternate ? _alternateRasterPlacements : _primaryRasterPlacements;
        List<TerminalRasterImagePlacement>? moved = placements is null ? null : new(placements.Count);
        if (placements is not null)
            foreach (TerminalRasterImagePlacement placement in placements)
                moved!.Add(placement.WithAnchorRow(checked(placement.AnchorRow + added)));
        // Validate anchor arithmetic before committing any of the storage.
        foreach (TrackedCell anchor in _trackedAnchors.Values)
            if (anchor.Alternate == alternate && anchor.Row >= 0) _ = checked(anchor.Row + added);

        rows.PrependRange(decoded); // Capacity growth may throw; no mutation until it succeeds.
        _hyperlinksById = owner._hyperlinksById;
        _hyperlinkIdsByUrl = owner._hyperlinkIdsByUrl;
        _hyperlinkIdentities = owner._hyperlinkIdentities;
        _nextHyperlinkId = owner._nextHyperlinkId;
        if (active) _rasterPlacements = moved!;
        else if (alternate) _alternateRasterPlacements = moved;
        else _primaryRasterPlacements = moved;
        foreach ((TerminalScreenAnchor token, TrackedCell anchor) in _trackedAnchors)
            if (anchor.Alternate == alternate && anchor.Row >= 0)
                CollectionsMarshal.GetValueRefOrNullRef(_trackedAnchors, token).Row += added;
        _anchorRevision++;
        // Bottom-relative scroll offsets stay unchanged: both the live viewport
        // and a scrolled-back viewport keep their original row identities.
        if (active) InvalidateViewport();
        return added;
    }
}
