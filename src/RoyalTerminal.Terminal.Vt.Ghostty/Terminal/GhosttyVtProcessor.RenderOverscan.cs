// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    private TerminalRenderOverscan _renderOverscan;
    private TerminalRenderOverscan _appliedRenderOverscan;
    private bool _renderOverscanRequestChanged;
    private ulong _renderViewportTop;
    private TerminalRow[]? _renderMirrorRows;
    private TerminalRow[]? _spareRenderMirrorRows;
    private ushort _renderMirrorAbove;
    private bool _renderMirrorAlternate;
    private Dictionary<GhosttyVtNative.GhosttyRenderStateRowId, TerminalRow>? _renderRowCache;
    private Dictionary<GhosttyVtNative.GhosttyRenderStateRowId, TerminalRow>? _spareRenderRowCache;

    /// <inheritdoc />
    public TerminalRenderOverscan RenderOverscan
    {
        get => _renderOverscan;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_renderOverscan == value) return;
            _renderOverscan = value;
            _renderOverscanRequestChanged = true;
            RefreshStateAndScreenFromNative();
        }
    }

    // Ghostty render.zig/c/render.zig: request changes affect the next update,
    // iterator indices include overscan, and row IDs identify storage, not text.
    // Clamp before native allocation: native page slack is not host scrollback.
    private void PrepareRenderOverscan()
    {
        TerminalRenderOverscan request = default;
        if (_renderOverscan != default &&
            _terminal.GetActiveScreen() != GhosttyVtNative.GhosttyTerminalScreen.Alternate)
        {
            GhosttyVtNative.GhosttyTerminalScrollbar scrollbar = _terminal.GetScrollbar();
            ulong total = Math.Max(scrollbar.Total, scrollbar.Length);
            ulong maxOffset = total - scrollbar.Length;
            ulong firstAccessible = maxOffset - Math.Min(maxOffset, (ulong)_screen.ScrollbackLimit);
            ulong top = Math.Min(scrollbar.Offset, maxOffset);
            request = new(
                (ushort)Math.Min((ulong)_renderOverscan.Above, top > firstAccessible ? top - firstAccessible : 0),
                (ushort)Math.Min((ulong)_renderOverscan.Below, maxOffset - top));
        }
        if (request == _appliedRenderOverscan) return;
        _renderState.SetOverscan(new() { Above = request.Above, Below = request.Below });
        _appliedRenderOverscan = request;
    }

    private void SyncOverscanRenderRows(bool fullRefresh,
        in GhosttyVtNative.GhosttyRenderStateColors colors,
        ReadOnlySpan<GhosttyVtNative.GhosttyColorRgb> palette)
    {
        GhosttyVtNative.GhosttyRenderStateOverscan captured = _renderState.GetCapturedOverscan();
        int count = captured.Above + _screen.ViewportRows + captured.Below;
        fullRefresh |= _renderMirrorRows is null || _renderMirrorRows.Length != count ||
            _renderMirrorAbove != captured.Above || _renderMirrorAlternate != _alternateScreen;
        if (!fullRefresh)
        {
            _renderState.BeginRows();
            while (_renderState.MoveNextDirtyRow(out _))
            {
                int y = _renderState.GetCurrentRowViewportY();
                int index = captured.Above + y;
                if ((uint)index >= (uint)count)
                    throw new InvalidOperationException("Native render row lies outside its completed capture.");
                // A replaced native page/row must not inherit the previous row's
                // managed storage ID. Restart a complete remap on identity change.
                if (!_renderRowCache!.TryGetValue(_renderState.GetCurrentRowId(), out TerminalRow? row) ||
                    !ReferenceEquals(row, _renderMirrorRows![index]))
                {
                    SyncOverscanRenderRows(true, colors, palette);
                    return;
                }
                PopulateCurrentRenderRow(row, y, colors, palette);
            }
            return;
        }

        if (_renderMirrorAlternate != _alternateScreen) _renderRowCache?.Clear();
        TerminalRow[] nextRows = _spareRenderMirrorRows is { } spare && spare.Length == count
            ? spare : new TerminalRow[count];
        Dictionary<GhosttyVtNative.GhosttyRenderStateRowId, TerminalRow> nextCache =
            _spareRenderRowCache ?? new(count);
        nextCache.Clear();
        if (count < nextCache.EnsureCapacity(0) / 4) nextCache.TrimExcess(count);
        nextCache.EnsureCapacity(count);
        _renderState.BeginRows();
        int indexInCapture = 0;
        while (_renderState.MoveNextRow())
        {
            int y = _renderState.GetCurrentRowViewportY();
            if (captured.Above + y != indexInCapture || indexInCapture >= count)
                throw new InvalidOperationException("Native render capture is not contiguous.");
            GhosttyVtNative.GhosttyRenderStateRowId id = _renderState.GetCurrentRowId();
            TerminalRow? row = null;
            if (id.IsValid) _renderRowCache?.TryGetValue(id, out row);
            if (row is null || row.Columns != _screen.Columns)
                row = new TerminalRow(_screen.Columns, _screen.DefaultForeground, _screen.DefaultBackground);
            // Even a retained ID can contain new text/styles. Full refresh never
            // skips extraction on ID equality (as with WT/xterm row reuse).
            PopulateCurrentRenderRow(row, y, colors, palette);
            nextRows[indexInCapture++] = row;
            if (id.IsValid) nextCache.Add(id, row);
        }
        if (indexInCapture != count)
            throw new InvalidOperationException("Native render capture has missing rows.");

        _screen.PublishExternalRenderRows(nextRows, captured.Above, captured.Below);
        _spareRenderMirrorRows = _renderMirrorRows?.Length == count ? _renderMirrorRows : null;
        if (_spareRenderMirrorRows is not null) Array.Clear(_spareRenderMirrorRows);
        _renderMirrorRows = nextRows;
        _spareRenderRowCache = _renderRowCache;
        _spareRenderRowCache?.Clear();
        if (_spareRenderRowCache is { } oldCache && count < oldCache.EnsureCapacity(0) / 4)
            oldCache.TrimExcess(count);
        _renderRowCache = nextCache;
        _renderMirrorAbove = captured.Above;
        _renderMirrorAlternate = _alternateScreen;
    }

    private void ReleaseRenderMirror()
    {
        _screen.ClearExternalRenderRows();
        _renderMirrorRows = null;
        _spareRenderMirrorRows = null;
        _renderRowCache = null;
        _spareRenderRowCache = null;
        _renderMirrorAbove = 0;
        _renderMirrorAlternate = false;
    }
}
