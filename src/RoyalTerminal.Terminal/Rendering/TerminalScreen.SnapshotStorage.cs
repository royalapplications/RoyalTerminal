// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Theming;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
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
}
