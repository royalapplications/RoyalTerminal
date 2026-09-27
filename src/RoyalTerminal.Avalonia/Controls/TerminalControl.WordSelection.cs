// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    internal bool TryReadExpandedWordSelection(out string? text)
    {
        text = null;
        if (_screen is null || !_hasAnchoredSelection || _mouseSelectionGranularity != MouseSelectionGranularity.Word ||
            _renderer is null || _renderer.SelectionIsRectangle || _selectionAnchorSpans.Length == 0 ||
            _vtProcessor is not ITerminalBufferSelectionExportSource source) return false;
        lock (_screen.SyncRoot)
        {
            // Expanded word spans are ordered, contiguous buffer rows. Use their
            // absolute endpoints instead of the renderer's clipped visible spans.
            var first = _selectionAnchorSpans[0];
            var last = _selectionAnchorSpans[^1];
            text = source.ReadBufferSelection(new(first.StartColumn, first.Row, last.EndColumn, last.Row), unwrap: true);
            return text is not null;
        }
    }
}
