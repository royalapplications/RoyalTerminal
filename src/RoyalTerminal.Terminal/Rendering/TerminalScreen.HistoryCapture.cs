// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalScreen
{
    internal void InvalidateHistoryLayout() => _rows.InvalidateHistoryLayout();

    internal TerminalHistoryBufferInfo GetHistoryBufferInfo(Guid bufferId)
    {
        int hidden = Math.Max(0, TotalRows - ViewportRows - MaxScrollOffset);
        return new(bufferId, _rows.HistoryLayoutEpoch, AlternateBufferActive, Columns, ViewportRows,
            new(checked(_rows.HistoryOrigin + hidden), TotalRows - hidden));
    }
}
