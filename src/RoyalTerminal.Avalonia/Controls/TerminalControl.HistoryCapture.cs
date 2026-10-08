// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Threading;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Reads active-buffer history metadata on the UI thread under the terminal lock.</summary>
    public TerminalHistoryStatus GetHistoryBufferInfo(out TerminalHistoryBufferInfo? buffer)
    {
        Dispatcher.UIThread.VerifyAccess();
        buffer = null;
        if (_screen is null || _vtProcessor is null) return TerminalHistoryStatus.BufferUnavailable;
        if (_vtProcessor is not ITerminalHistorySnapshotSource source) return TerminalHistoryStatus.Unsupported;
        lock (_screen.SyncRoot) return source.GetHistoryBufferInfo(out buffer);
    }

    /// <summary>Captures owned bounded history on the UI thread without changing terminal presentation.</summary>
    /// <remarks>Results may be retained and paged off-thread after terminal disposal.</remarks>
    public TerminalHistoryStatus CaptureHistory(in TerminalHistoryCaptureRequest request,
        out TerminalHistorySnapshot? snapshot, CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        snapshot = null;
        if (_screen is null || _vtProcessor is null) return TerminalHistoryStatus.BufferUnavailable;
        if (_vtProcessor is not ITerminalHistorySnapshotSource source) return TerminalHistoryStatus.Unsupported;
        lock (_screen.SyncRoot) return source.CaptureHistory(request, out snapshot, cancellationToken);
    }
}
