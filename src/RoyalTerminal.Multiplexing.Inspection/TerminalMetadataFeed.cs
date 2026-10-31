// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>One immutable terminal metadata transition. Text originates in the terminal and is untrusted.</summary>
public sealed record TerminalMetadataEvent(long Sequence, string Name, string? Text = null, int? ExitCode = null,
    string? RecordId = null, TerminalProgramStatus? Record = null, string? Reason = null);

/// <summary>A bounded cursor page. NextSequence is the last delivered event, never a cursor past undelivered entries.</summary>
public sealed record TerminalMetadataPage(long FirstSequence, long LastSequence, long NextSequence, bool Ended, IReadOnlyList<TerminalMetadataEvent> Events);

/// <summary>Per-block metadata history. It is separate from ordered VT byte delivery and cannot be used as a VT replay cursor.</summary>
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

    internal void Bell() => Add("terminal.bell");
    internal void Title(string title) => Add("terminal.title_changed", text: title.Length > 2048 ? title[..2048] : title);
    internal void ChildExited(int code) { lock (_gate) { if (!_ended) { AddLocked("terminal.child_exited", exitCode: code); EndLocked(); } } }
    internal void End() { lock (_gate) EndLocked(); }
    private void EndLocked() { _ended = true; _signal?.TrySetResult(); _signal = null; }

    internal void StatusChanged(IReadOnlyList<TerminalProgramStatus> records)
    {
        // Callback runs at a real parser transition. Two changes within the same PTY read are not collapsed.
        lock (_gate)
        {
            if (_ended) return;
            HashSet<string> present = new(StringComparer.Ordinal);
            foreach (TerminalProgramStatus item in records)
            {
                present.Add(item.Id);
                if (_records.TryGetValue(item.Id, out var old) && old == item) continue;
                _records[item.Id] = item; AddLocked("terminal.program_status_changed", recordId:item.Id, record:item, reason:"set");
            }
            foreach (string id in _records.Keys.Where(id => !present.Contains(id)).ToArray())
            { _records.Remove(id); AddLocked("terminal.program_status_changed", recordId:id, reason:"removed"); }
        }
    }
    private void Add(string name,string? text=null) { lock (_gate) if (!_ended) AddLocked(name,text); }
    private void AddLocked(string name,string? text=null,int? exitCode=null,string? recordId=null,TerminalProgramStatus? record=null,string? reason=null)
    {
        TerminalMetadataEvent item = new(++_last,name,text,exitCode,recordId,record,reason);
        int charge = 160 + 2*((text?.Length??0)+(recordId?.Length??0)+(record?.Title.Length??0)+(record?.Message.Length??0)+(record?.App.Length??0));
        _history.Enqueue((item,charge)); _bytes+=charge;
        while(_history.Count>MaxEvents||_bytes>MaxBytes) _bytes-=_history.Dequeue().Charge;
        _signal?.TrySetResult();_signal=null;
    }

    /// <summary>Omit after to get a start-at-now cursor without returning history. Supply zero to request all retained events from creation;
    /// a lost prefix or cursor ahead of this block is an explicit error. Waits are bounded and cancellation is per reader.</summary>
    public async ValueTask<TerminalMetadataPage> ReadAsync(long? after=null,int limit=64,int timeoutMs=0,CancellationToken cancellationToken=default)
    {
        if(after<0 || limit is <1 or >128 || timeoutMs is <0 or >25000) throw new ArgumentOutOfRangeException(nameof(after),"Invalid cursor, limit (1..128), or timeout (0..25000 ms).");
        cancellationToken.ThrowIfCancellationRequested(); Task wait;
        lock(_gate)
        {
            if(after is null) return Page(_last,limit);
            if(after.Value!=_last||_ended||timeoutMs==0) return Page(after.Value,limit);
            if(_waiters>=MaxWaiters) throw new MultiplexerException(MultiplexerErrorCode.CapacityExceeded,"Too many metadata readers for this block.");
            ++_waiters;wait=(_signal??=new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        try { try { await wait.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs),cancellationToken).ConfigureAwait(false); } catch(TimeoutException) { } }
        finally { lock(_gate)--_waiters; }
        cancellationToken.ThrowIfCancellationRequested();lock(_gate)return Page(after.Value,limit);
    }
    private TerminalMetadataPage Page(long after,int limit)
    {
        long first=_history.TryPeek(out var head)?head.Event.Sequence:_last+1;
        if(after<first-1) throw new MultiplexerException(MultiplexerErrorCode.HistoryGap,"The metadata cursor precedes retained history.");
        if(after>_last) throw new MultiplexerException(MultiplexerErrorCode.Conflict,"The metadata cursor is ahead of this block.");
        TerminalMetadataEvent[] events=_history.Where(i=>i.Event.Sequence>after).Take(limit).Select(i=>i.Event).ToArray();
        long next=events.Length==0?after:events[^1].Sequence;return new(first,_last,next,_ended&&next==_last,events);
    }
}
