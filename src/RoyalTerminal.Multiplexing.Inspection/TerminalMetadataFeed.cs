// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>One immutable transition. All terminal-originated text is untrusted.</summary>
public sealed record TerminalMetadataEvent(long Sequence, string Name, string? Text = null, int? ExitCode = null,
    string? RecordId = null, TerminalProgramStatus? Record = null, string? Reason = null)
{
    /// <summary>Raw directory report; never resolved or opened on the server.</summary>
    public string? Pwd { get; init; }
    /// <summary>A request for a notification, not evidence of delivery to an OS.</summary>
    public TerminalDesktopNotification? Notification { get; init; }
    /// <summary>Reported progress; null percentage is distinct from zero.</summary>
    public TerminalProgressReport? ProgressReport { get; init; }
}

/// <summary>A bounded cursor page. NextSequence is the last delivered event, never a cursor past undelivered entries.</summary>
public sealed record TerminalMetadataPage(long FirstSequence, long LastSequence, long NextSequence, bool Ended, IReadOnlyList<TerminalMetadataEvent> Events);

/// <summary>Per-block metadata history. Its sequence space is independent of the VT replay stream.</summary>
public sealed class TerminalMetadataFeed
{
    private const int MaxEvents = 512, MaxBytes = 512 * 1024, MaxWaiters = 32;
    private readonly object _gate = new();
    private readonly Queue<(TerminalMetadataEvent Event, int Charge)> _history = [];
    private readonly Dictionary<string, TerminalProgramStatus> _records = new(StringComparer.Ordinal);
    private int _bytes, _waiters;
    private long _last;
    private bool _ended;
    private TaskCompletionSource? _signal;

    internal void Observe(BasicVtProcessor processor)
    {
        processor.BellCallback = () => Add(new(0, "terminal.bell"));
        processor.TitleCallback = title => Add(new(0, "terminal.title_changed", Text: Bound(title, 2048)));
        processor.WorkingDirectoryCallback = pwd => Add(new(0, "terminal.pwd_changed") { Pwd = Bound(pwd, 4096) });
        processor.DesktopNotificationCallback = item => Add(new(0, "terminal.desktop_notification")
        { Notification = new(Bound(item.Title, 512), Bound(item.Body, 2048)) });
        processor.ProgressReportCallback = report => Add(new(0, "terminal.progress_report") { ProgressReport = report });
        processor.ProgramStatusChangedCallback = () => StatusChanged(processor.ProgramStatuses);
    }

    // Bound UTF-16 storage without truncating a valid surrogate pair in half.
    private static string Bound(string value, int limit) => value.Length <= limit ? value :
        value[..(char.IsHighSurrogate(value[limit - 1]) ? limit - 1 : limit)];

    internal void ChildExited(int code)
    {
        lock (_gate)
        {
            if (_ended) return;
            AddLocked(new(0, "terminal.child_exited", ExitCode: code));
            EndLocked();
        }
    }

    internal void End() { lock (_gate) EndLocked(); }
    private void EndLocked() { _ended = true; _signal?.TrySetResult(); _signal = null; }

    private void StatusChanged(IReadOnlyList<TerminalProgramStatus> records)
    {
        lock (_gate)
        {
            if (_ended) return;
            HashSet<string> present = new(StringComparer.Ordinal);
            foreach (TerminalProgramStatus item in records)
            {
                present.Add(item.Id);
                if (_records.TryGetValue(item.Id, out var old) && old == item) continue;
                _records[item.Id] = item;
                AddLocked(new(0, "terminal.program_status_changed", RecordId: item.Id, Record: item, Reason: "set"));
            }
            foreach (string id in _records.Keys.Where(id => !present.Contains(id)).ToArray())
            {
                _records.Remove(id);
                AddLocked(new(0, "terminal.program_status_changed", RecordId: id, Reason: "removed"));
            }
        }
    }

    private void Add(TerminalMetadataEvent item) { lock (_gate) if (!_ended) AddLocked(item); }

    private void AddLocked(TerminalMetadataEvent item)
    {
        int charge = 192 + 2 * ((item.Text?.Length ?? 0) + (item.RecordId?.Length ?? 0) +
            (item.Record?.Title.Length ?? 0) + (item.Record?.Message.Length ?? 0) + (item.Record?.App.Length ?? 0) +
            (item.Pwd?.Length ?? 0) + (item.Notification?.Title.Length ?? 0) + (item.Notification?.Body.Length ?? 0));
        _history.Enqueue((item with { Sequence = ++_last }, charge));
        _bytes += charge;
        while (_history.Count > MaxEvents || _bytes > MaxBytes) _bytes -= _history.Dequeue().Charge;
        _signal?.TrySetResult();
        _signal = null;
    }

    /// <summary>Omit after to start at the current cursor. Missing history and a cursor ahead of the producer are errors.</summary>
    public async ValueTask<TerminalMetadataPage> ReadAsync(long? after = null, int limit = 64, int timeoutMs = 0, CancellationToken cancellationToken = default)
    {
        if (after < 0 || limit is < 1 or > 128 || timeoutMs is < 0 or > 25000)
            throw new ArgumentOutOfRangeException(nameof(after), "Invalid cursor, limit (1..128), or timeout (0..25000 ms).");
        cancellationToken.ThrowIfCancellationRequested();
        Task wait;
        lock (_gate)
        {
            if (after is null) return Page(_last, limit);
            if (after.Value != _last || _ended || timeoutMs == 0) return Page(after.Value, limit);
            if (_waiters >= MaxWaiters) throw new MultiplexerException(MultiplexerErrorCode.CapacityExceeded, "Too many metadata readers for this block.");
            ++_waiters;
            wait = (_signal ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        try { await wait.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { }
        finally { lock (_gate) --_waiters; }
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Page(after.Value, limit);
    }

    private TerminalMetadataPage Page(long after, int limit)
    {
        long first = _history.TryPeek(out var head) ? head.Event.Sequence : _last + 1;
        if (after < first - 1) throw new MultiplexerException(MultiplexerErrorCode.HistoryGap, "The metadata cursor precedes retained history.");
        if (after > _last) throw new MultiplexerException(MultiplexerErrorCode.Conflict, "The metadata cursor is ahead of this block.");
        TerminalMetadataEvent[] events = _history.Where(i => i.Event.Sequence > after).Take(limit).Select(i => i.Event).ToArray();
        long next = events.Length == 0 ? after : events[^1].Sequence;
        return new(first, _last, next, _ended && next == _last, events);
    }
}
