// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    // Ghostty PageList.resizeCols preserves the active rows below the cursor,
    // discounting additional wrapped rows above it. This is VT cursor policy;
    // generic screen-only resize and ConPTY viewport preservation stay separate.
    private readonly record struct ReflowCursorPadding(int RemainingRows, int WrappedRows);

    private ReflowCursorPadding CaptureReflowCursorPadding(int absoluteRow, int viewportRow, int viewportRows)
        => new(Math.Max(0, viewportRows - viewportRow - 1), CountReflowCursorWraps(absoluteRow, viewportRows));

    private int CountReflowCursorWraps(int absoluteRow, int viewportRows)
    {
        int count = 0;
        for (int i = Math.Max(0, _rows.Count - viewportRows); i <= absoluteRow && i < _rows.Count; i++)
            if (_rows[i].IsWrapContinuation) count++;
        return count;
    }

    private void RestoreReflowCursorPadding(ReflowCursorPadding padding, int absoluteRow, int viewportRows, int columns)
    {
        // resizeCols first fills the active area, then only preserves a cursor
        // that still resolves inside it. Height growth/shrink follows afterward
        // for widening, but precedes reflow for narrowing.
        while (_rows.Count < viewportRows)
            _rows.Add(new TerminalRow(columns, DefaultForeground, DefaultBackground));
        int activeTop = _rows.Count - viewportRows;
        if (absoluteRow < activeTop || absoluteRow >= _rows.Count) return;
        int additionalWraps = Math.Max(0, CountReflowCursorWraps(absoluteRow, viewportRows) - padding.WrappedRows);
        int currentRemaining = _rows.Count - absoluteRow - 1;
        int required = Math.Max(0, padding.RemainingRows - additionalWraps - currentRemaining);
        AppendBlankResizeRows(required, columns);
    }

    private void AppendBlankResizeRows(int count, int columns)
    {
        for (int i = 0; i < count; i++)
            _rows.Add(new TerminalRow(columns, DefaultForeground, DefaultBackground));
    }
}
