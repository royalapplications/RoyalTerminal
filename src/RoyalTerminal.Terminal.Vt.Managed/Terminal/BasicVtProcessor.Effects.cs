// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private int _unknownSequenceMaxBytes = 4096;
    // Stream-effect state, not screen/snapshot state. Track it even without a
    // callback, like Ghostty Handler.progress_active. The C reset API does not
    // reset this handler flag; only protocol RIS clears active host progress.
    private bool _progressActive;

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
        _progressActive = report.State != TerminalProgressState.Remove;
        ProgressReportCallback?.Invoke(report);
    }

    private void ClearActiveProgressReport()
    {
        if (!_progressActive) return;
        _progressActive = false;
        ProgressReportCallback?.Invoke(new TerminalProgressReport(TerminalProgressState.Remove, null));
    }
}
