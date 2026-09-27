// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    /// <inheritdoc />
    public Action<TerminalWindowResizeRequest>? WindowResizeCallback { get; set; }

    private int _unknownSequenceMaxBytes = 4096;

    /// <inheritdoc />
    public int UnknownSequenceMaxBytes
    {
        get => _unknownSequenceMaxBytes;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _unknownSequenceMaxBytes = value;
            if (!_apcUnknownRecognized) _apcUnknownCapture.Reset(value);
        }
    }

    internal Action<ManagedUnknownApcAllocation>? UnknownApcAllocationCheckpoint
    {
        get => _apcUnknownCapture.AllocationCheckpoint;
        set => _apcUnknownCapture.AllocationCheckpoint = value;
    }

    private void PublishProgressReport(TerminalProgressReport report)
    {
        ProgressReportCallback?.Invoke(report);
    }

    private void ClearActiveProgressReport()
    {
        // Ghostty stream_terminal.Handler emits remove on every protocol RIS,
        // including when no progress report has been observed by this handler.
        // Programmatic grid reset and snapshot restoration emit no host effect.
        ProgressReportCallback?.Invoke(new TerminalProgressReport(TerminalProgressState.Remove, null));
    }
}
