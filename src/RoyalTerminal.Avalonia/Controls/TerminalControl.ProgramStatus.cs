// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Threading;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Read-only program statuses published on the UI thread.</summary>
    public static readonly DirectProperty<TerminalControl, IReadOnlyList<TerminalProgramStatus>> ProgramStatusesProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, IReadOnlyList<TerminalProgramStatus>>(
            nameof(ProgramStatuses), control => control.ProgramStatuses);

    private IReadOnlyList<TerminalProgramStatus> _publishedProgramStatuses = [];
    private ITerminalProgramStatusSource? _programStatusSource;
    private int _programStatusUpdatePending;

    /// <summary>Immutable status snapshot with inherited application names resolved.
    /// Text is untrusted plain text. Updated on the UI thread, coalescing input bursts.</summary>
    public IReadOnlyList<TerminalProgramStatus> ProgramStatuses => _publishedProgramStatuses;

    /// <summary>Raised on the UI thread after a new program-status snapshot is published.</summary>
    public event EventHandler? ProgramStatusesChanged;

    private void BindProgramStatusSource(IVtProcessor processor)
    {
        if (_programStatusSource is not null) _programStatusSource.ProgramStatusChangedCallback = null;
        _programStatusSource = processor as ITerminalProgramStatusSource;
        if (_programStatusSource is not null) _programStatusSource.ProgramStatusChangedCallback = QueueProgramStatusUpdate;
        QueueProgramStatusUpdate();
    }

    private void QueueProgramStatusUpdate()
    {
        if (Interlocked.Exchange(ref _programStatusUpdatePending, 1) != 0) return;
        Dispatcher.UIThread.Post(PublishProgramStatuses, DispatcherPriority.Background);
    }

    private void PublishProgramStatuses()
    {
        Interlocked.Exchange(ref _programStatusUpdatePending, 0);
        IReadOnlyList<TerminalProgramStatus> snapshot = [];
        if (_screen is not null)
        {
            lock (_screen.SyncRoot)
            {
                if (_programStatusSource is { } source && source.ProgramStatuses.Count > 0)
                {
                    TerminalProgramStatus[] records = new TerminalProgramStatus[source.ProgramStatuses.Count];
                    for (int i = 0; i < records.Length; i++)
                    {
                        TerminalProgramStatus record = source.ProgramStatuses[i];
                        records[i] = record with { App = source.GetProgramStatusApplication(record.Id) };
                    }
                    snapshot = Array.AsReadOnly(records);
                }
            }
        }
        SetAndRaise(ProgramStatusesProperty, ref _publishedProgramStatuses, snapshot);
        ProgramStatusesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndProgramStatusProcess()
    {
        if (_screen is null) return;
        lock (_screen.SyncRoot) _programStatusSource?.NotifyProgramStatusProcessExit();
    }
}
