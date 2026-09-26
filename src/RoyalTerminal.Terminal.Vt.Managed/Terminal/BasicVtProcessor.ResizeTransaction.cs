// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

// Allocation-boundary tripwires, like Ghostty Screen.resize's tripwire checks.
// Internal and per processor: no global allocator, reflection or public knobs.
internal enum ManagedResizeCheckpoint
{
    Staged, PrimaryRows, PrimaryCursor, AlternateRows, AlternateCursor, Layout, Graphics, Ready,
}

public sealed partial class BasicVtProcessor
{
    internal Action<ManagedResizeCheckpoint>? ResizeCheckpoint { get; set; }

    // Only fields touched by resize belong here. Screen storage is staged with
    // COW; Kitty state owns copied mutable metadata but shares immutable pixels.
    // Rollback is assignment-only and must remain usable after allocation failure.
    private readonly struct ResizeRollbackState(BasicVtProcessor owner)
    {
        private readonly TerminalScreen _screen = owner._screen;
        private readonly RenderHoldState? _hold = owner._renderHold;
        private readonly ManagedKittyGraphicsStore _kitty = owner._kittyStore;
        private readonly HashSet<int> _tabs = owner._tabStops;
        private readonly int _tabColumns = owner._tabStopColumns;
        private readonly (int Column, int Row, bool Wrap) _cursor = (owner._cursorCol, owner._cursorRow, owner._delayedWrap);
        private readonly (int Column, int Row, bool Wrap) _primary = (owner._savedMainCursorCol, owner._savedMainCursorRow, owner._savedMainDelayedWrap);
        private readonly (int Column, int Row, bool Wrap) _alternate = (owner._savedAlternateCursorCol, owner._savedAlternateCursorRow, owner._savedAlternateDelayedWrap);
        private readonly SavedCursorState? _primarySaved = owner._primarySavedCursor;
        private readonly SavedCursorState? _alternateSaved = owner._alternateSavedCursor;
        private readonly (int Active, int Primary, int Alternate) _links = (owner._currentHyperlinkId, owner._snapshotPrimaryHyperlink, owner._snapshotAlternateHyperlink);
        private readonly (uint Primary, uint Alternate) _counters = (owner._primaryHyperlinkImplicitCounter, owner._alternateHyperlinkImplicitCounter);
        private readonly (int Top, int Bottom, int Left, int Right) _margins = (owner._scrollTop, owner._scrollBottom, owner._scrollLeft, owner._scrollRight);
        private readonly (uint Width, uint Height, uint CellWidth, uint CellHeight) _pixels = (owner._widthPx, owner._heightPx, owner._reportCellWidthPx, owner._reportCellHeightPx);
        private readonly TimeSpan? _animationDelay = owner._animationNextTickDelay;
        private readonly long _animationTimestamp = owner._animationTickTimestamp;

        internal void Restore(BasicVtProcessor owner)
        {
            owner._screen = _screen;
            owner._renderHold = _hold;
            owner._kittyStore = _kitty;
            owner._tabStops = _tabs;
            owner._tabStopColumns = _tabColumns;
            (owner._cursorCol, owner._cursorRow, owner._delayedWrap) = _cursor;
            (owner._savedMainCursorCol, owner._savedMainCursorRow, owner._savedMainDelayedWrap) = _primary;
            (owner._savedAlternateCursorCol, owner._savedAlternateCursorRow, owner._savedAlternateDelayedWrap) = _alternate;
            owner._primarySavedCursor = _primarySaved;
            owner._alternateSavedCursor = _alternateSaved;
            (owner._currentHyperlinkId, owner._snapshotPrimaryHyperlink, owner._snapshotAlternateHyperlink) = _links;
            (owner._primaryHyperlinkImplicitCounter, owner._alternateHyperlinkImplicitCounter) = _counters;
            (owner._scrollTop, owner._scrollBottom, owner._scrollLeft, owner._scrollRight) = _margins;
            (owner._widthPx, owner._heightPx, owner._reportCellWidthPx, owner._reportCellHeightPx) = _pixels;
            owner._animationNextTickDelay = _animationDelay;
            owner._animationTickTimestamp = _animationTimestamp;
        }
    }
}
