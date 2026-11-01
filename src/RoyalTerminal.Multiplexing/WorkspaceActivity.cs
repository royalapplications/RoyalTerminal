// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;

namespace RoyalTerminal.Multiplexing;

/// <summary>One immutable transition. Sequence belongs only to the journal being read.</summary>
public sealed record WorkspaceActivity(long Sequence, string Scope, string Type, Guid SessionId,
    long? Revision = null, Guid? WindowId = null, Guid? BlockId = null, string? Label = null,
    Guid? ClientId = null, JsonElement? Detail = null);

/// <summary>Resumable journal page; an empty, non-ended page is a timeout or initial fence.</summary>
public sealed record WorkspaceActivityPage(long FirstSequence, long LastSequence, long NextSequence,
    bool Ended, IReadOnlyList<WorkspaceActivity> Events);

/// <summary>Independent bounded server, session and all-activity cursors. The host authorizes reads.</summary>
public sealed class WorkspaceActivityHub
{
    private const int Capacity = 1024, ByteLimit = 2 << 20, MaxWaiters = 128;
    private readonly object _gate = new();
    private readonly Journal _server = new(), _all = new();
    private readonly Dictionary<Guid, Journal> _sessions = [];
    private readonly Queue<Guid> _retired = [];
    private int _waiters;
    private bool _ended;

    internal void Open(Guid id, Guid window, string label)
    {
        lock (_gate)
        {
            var session = new Journal(); _sessions.Add(id, session);
            Write(new(0, "server", "session_created", id, Label: label), null, server: true);
            Write(new(0, "session", "window_created", id, 1, window, Label: "Main"), session);
            Write(new(0, "session", "session_view_changed", id, 1), session, server: true);
        }
    }

    internal void Change(Guid id, long revision, string? type, Guid? window, Guid? block, string? label)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var s) || s.Ended) return;
            if (type is not null) Write(new(0,"session",type,id,revision,window,block,label),s);
            Write(new(0,"session","session_view_changed",id,revision),s,server:true);
        }
    }

    internal void Close(Guid id)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var s) || s.Ended) return;
            Write(new(0,"server","session_destroyed",id),s,server:true);
            s.End(); _retired.Enqueue(id);
            // Tombstones let a slow subscriber drain the final page without leaking unbounded history.
            if (_retired.Count > 8) _sessions.Remove(_retired.Dequeue());
        }
    }

    internal void Complete()
    {
        lock (_gate) { _ended = true; _all.End(); _server.End(); foreach (var s in _sessions.Values) s.End(); }
    }

    /// <summary>Forward an already validated metadata record from an admitted block. Never use this for raw VT output.</summary>
    public void RecordTerminal(Guid session, Guid block, JsonElement detail, int retainedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retainedBytes);
        lock (_gate)
        {
            if (_ended || !_sessions.TryGetValue(session,out var s) || s.Ended) return;
            if (retainedBytes > ByteLimit - 256) { Gap(s, 1); return; }
            Write(new(0,"session","block_event",session, BlockId:block, Detail:detail.Clone()),s,extra:retainedBytes);
        }
    }

    /// <summary>Invalidate the prefix when an evicted child startup event cannot be incorporated losslessly.</summary>
    public void RecordTerminalGap(Guid session, long missing)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(missing,1);
        lock (_gate) if (_sessions.TryGetValue(session,out var s) && !s.Ended) Gap(s, missing);
    }
    private void Gap(Journal s, long missing) { s.Gap(missing); _all.Gap(missing); }

    /// <summary>Record an actual authenticated attachment transition. Never supply credentials as metadata.</summary>
    public void RecordClient(bool connected, Guid session, Guid block, Guid client, JsonElement detail)
    {
        lock (_gate)
        {
            if (_ended || !_sessions.TryGetValue(session,out var s) || s.Ended) return;
            var owned = detail.Clone();
            Write(new(0,"server",connected?"client_joined":"client_left",session,BlockId:block,ClientId:client,Detail:owned),null);
            Write(new(0,"session",connected?"client_connected":"client_disconnected",session,BlockId:block,ClientId:client,Detail:owned),s);
        }
    }

    private void Write(WorkspaceActivity item, Journal? session, bool server=false, int extra=0)
    {
        int charge = 256 + (item.Label?.Length ?? 0) * 2 + extra;
        _all.Add(item,charge); session?.Add(item,charge); if (server) _server.Add(item,charge);
    }

    /// <summary>Read one session, the default server feed, or all activity. Registering a waiter and checking its fence are atomic.</summary>
    public async ValueTask<WorkspaceActivityPage> ReadAsync(Guid? sessionId=null,bool all=false,long? after=null,
        int limit=64,int timeoutMs=0,CancellationToken cancellationToken=default)
    {
        if (sessionId.HasValue && all || after < 0 || limit is < 1 or > 128 || timeoutMs is < 0 or > 25000)
            throw new ArgumentException("Invalid activity scope, cursor, limit or timeout.");
        cancellationToken.ThrowIfCancellationRequested();
        Journal feed; Task wait;
        lock (_gate)
        {
            feed = sessionId is Guid id ? _sessions.TryGetValue(id, out var found) ? found :
                throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Session activity is unavailable.") : all ? _all : _server;
            if (!after.HasValue || after.Value != feed.Last || timeoutMs == 0 || feed.Ended) return feed.Page(after ?? feed.Last,limit);
            if (_waiters >= MaxWaiters || feed.Waiters >= 32) throw new MultiplexerException(MultiplexerErrorCode.CapacityExceeded,"Too many activity readers.");
            _waiters++; feed.Waiters++; wait = (feed.Signal ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        try { await wait.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs),cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { }
        finally { lock (_gate) { feed.Waiters--; _waiters--; } }
        cancellationToken.ThrowIfCancellationRequested(); lock (_gate) return feed.Page(after.Value,limit);
    }

    // A dense suffix ring: O(1) cursor lookup, O(page size) read. The hub gate protects all members.
    private sealed class Journal
    {
        private readonly (WorkspaceActivity? Item,int Charge)[] _ring = new (WorkspaceActivity?,int)[Capacity];
        private int _bytes,_count;
        internal long Last; internal bool Ended; internal int Waiters; internal TaskCompletionSource? Signal;
        private static int Slot(long sequence) => (int)((sequence-1) % Capacity);
        private void Wake() { Signal?.TrySetResult(); Signal=null; }
        internal void End() { Ended=true; Wake(); }
        internal void Gap(long missing) { Array.Clear(_ring); _count=0; _bytes=0; Last=checked(Last+missing); Wake(); }
        internal void Add(WorkspaceActivity item,int charge)
        {
            while (_count>0 && (_count==Capacity || _bytes+charge>ByteLimit))
            { int old=Slot(Last-_count+1); _bytes-=_ring[old].Charge; _ring[old]=default; _count--; }
            Last=checked(Last+1); _ring[Slot(Last)]=(item with {Sequence=Last},charge); _count++; _bytes+=charge; Wake();
        }
        internal WorkspaceActivityPage Page(long after,int limit)
        {
            if (after<Last-_count) throw new MultiplexerException(MultiplexerErrorCode.HistoryGap,"Activity history has expired; request a new fence explicitly.");
            if (after>Last) throw new MultiplexerException(MultiplexerErrorCode.Conflict,"Activity cursor is ahead of this scope.");
            int count=(int)Math.Min(Last-after,limit); var result=new WorkspaceActivity[count];
            for(int i=0;i<count;i++)result[i]=_ring[Slot(after+i+1)].Item!;
            return new(Last-_count+1,Last,after+count,Ended && after+count==Last,result);
        }
    }
}
