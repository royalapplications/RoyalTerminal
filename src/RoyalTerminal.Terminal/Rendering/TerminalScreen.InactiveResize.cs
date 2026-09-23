// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    /// <summary>
    /// Temporarily exposes an existing dormant buffer to the same resize machinery.
    /// Caller holds the screen lock, must dispose the scope, and must not publish
    /// or execute terminal input until the original active buffer is restored.
    /// No screen-switch protocol semantics or row normalization run here.
    /// </summary>
    internal InactiveResizeScope EnterInactiveResize(int oldColumns, int oldRows)
        => new(this, oldColumns, oldRows);

    internal readonly ref struct InactiveResizeScope
    {
        private readonly TerminalScreen _screen;
        private readonly TerminalRowBuffer _rows;
        private readonly Dictionary<int, TerminalRasterImageSource> _images;
        private readonly List<TerminalRasterImagePlacement> _placements;
        private readonly int _columns;
        private readonly int _viewportRows;
        private readonly int _scrollOffset;
        private readonly bool _alternate;

        internal bool Available { get; }

        internal InactiveResizeScope(TerminalScreen screen, int oldColumns, int oldRows)
        {
            _screen = screen;
            _rows = screen._rows;
            _images = screen._rasterImagesById;
            _placements = screen._rasterPlacements;
            _columns = screen.Columns;
            _viewportRows = screen.ViewportRows;
            _scrollOffset = screen._viewportTop;
            _alternate = screen._alternateBufferActive;
            TerminalRowBuffer? inactive = _alternate ? screen._primaryRows : screen._alternateRows;
            Available = inactive is not null;
            if (!Available) return;

            screen._rows = inactive!;
            screen._rasterImagesById = (_alternate ? screen._primaryRasterImagesById : screen._alternateRasterImagesById) ?? [];
            screen._rasterPlacements = (_alternate ? screen._primaryRasterPlacements : screen._alternateRasterPlacements) ?? [];
            screen._alternateBufferActive = !_alternate;
            screen.Columns = oldColumns;
            screen.ViewportRows = oldRows;
            screen.ScrollOffset = _alternate ? screen._primaryScrollOffset : 0;
        }

        public void Dispose()
        {
            if (!Available) return;
            if (_alternate)
            {
                _screen._primaryRows = _screen._rows;
                _screen._primaryRasterImagesById = _screen._rasterImagesById;
                _screen._primaryRasterPlacements = _screen._rasterPlacements;
                _screen._primaryScrollOffset = _screen._viewportTop;
            }
            else
            {
                _screen._alternateRows = _screen._rows;
                _screen._alternateRasterImagesById = _screen._rasterImagesById;
                _screen._alternateRasterPlacements = _screen._rasterPlacements;
            }

            _screen._rows = _rows;
            _screen._rasterImagesById = _images;
            _screen._rasterPlacements = _placements;
            _screen._alternateBufferActive = _alternate;
            _screen.Columns = _columns;
            _screen.ViewportRows = _viewportRows;
            _screen._viewportTop = _scrollOffset;
        }
    }
}
