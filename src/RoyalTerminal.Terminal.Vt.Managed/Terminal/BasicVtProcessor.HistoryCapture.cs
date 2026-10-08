// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor : ITerminalHistorySnapshotSource, ITerminalHistoryRowReader
{
    private readonly Guid _historyPrimaryId = Guid.NewGuid();
    private readonly Guid _historyAlternateId = Guid.NewGuid();
    private bool _historyDisposed;

    /// <inheritdoc />
    public TerminalHistoryStatus GetHistoryBufferInfo(out TerminalHistoryBufferInfo? buffer)
    {
        buffer = null;
        if (_historyDisposed) return TerminalHistoryStatus.Disposed;
        if (_screen.SnapshotMutationFailed) return TerminalHistoryStatus.BufferUnavailable;
        buffer = _screen.GetHistoryBufferInfo(_screen.AlternateBufferActive ? _historyAlternateId : _historyPrimaryId);
        return TerminalHistoryStatus.Success;
    }

    /// <inheritdoc />
    public TerminalHistoryStatus CaptureHistory(in TerminalHistoryCaptureRequest request,
        out TerminalHistorySnapshot? snapshot, CancellationToken cancellationToken = default)
    {
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        snapshot = null;
        TerminalHistoryStatus status = GetHistoryBufferInfo(out TerminalHistoryBufferInfo? buffer);
        return status == TerminalHistoryStatus.Success
            ? TerminalHistoryCapture.Capture(this, buffer!, request, out snapshot, cancellationToken) : status;
    }

    bool ITerminalHistoryRowReader.TryReadHistoryRow(int rowIndex, int maxCharacters,
        CancellationToken cancellationToken, out TerminalHistoryRow? result)
    {
        int hidden = Math.Max(0, _screen.TotalRows - _screen.ViewportRows - _screen.MaxScrollOffset);
        TerminalRow row = _screen.GetRow(hidden + rowIndex);
        TerminalHistoryRowBuilder builder = new(maxCharacters);
        ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;
        result = null;
        for (int column = 0; column < cells.Length; column++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ref readonly TerminalCell cell = ref cells[column];
            if (cell.Width == 0 || cell.IsWideSpacerHead) continue;
            if (!cell.HasContent) { builder.ErasedCell(); continue; }
            bool fits = !string.IsNullOrEmpty(cell.Grapheme)
                ? builder.Append(cell.Grapheme) : builder.Append((uint)cell.Codepoint);
            if (!fits) return false;
        }
        result = builder.Build(row.WrapsToNext);
        return true;
    }
}
