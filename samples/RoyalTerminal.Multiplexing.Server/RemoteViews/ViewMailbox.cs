// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Security.Cryptography;
using System.Text;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record ViewIdentity(Guid Id, Guid SessionId, string Label, string Kind);
internal sealed record ViewLease(ViewIdentity View, string ReaderKey);
internal sealed record ViewAction(string Name, Guid? BlockId = null, Guid? WindowId = null, string? Direction = null);
internal sealed record ViewBatch(long Sequence, ViewAction[] Actions);
internal sealed record ViewDelivery(long Cursor, ViewBatch[] Batches);
internal sealed record ViewReceipt(Guid ClientId, long Sequence);

/// <summary>One mailbox per explicitly consenting UI, never per PTY. Retain batches until the receiver acknowledges its cursor.</summary>
internal sealed class ViewMailbox(TimeProvider clock, Func<AccessGrant,bool> active)
{
    private const int MaxViews = 128, MaxPerGrant = 8, MaxBatches = 32;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid,Entry> _views = [];
    private sealed class Entry(ViewIdentity view, AccessGrant owner, byte[] secret, long now)
    {
        internal readonly ViewIdentity View = view;
        internal readonly AccessGrant Owner = owner;
        internal readonly byte[] Secret = secret;
        internal long Seen = now, Issued, Offered, Acknowledged;
        internal readonly Queue<ViewBatch> Queue = [];
        internal bool Reading;
        internal TaskCompletionSource? Wake;
    }
    internal ViewLease Open(Guid session, string label, string kind, AccessGrant owner)
    {
        label = label.Trim();
        if (label.Length is 0 or > 80 || label.Any(char.IsControl) || kind is not ("browser" or "native" or "mobile")) throw new ArgumentException("Invalid client kind or label.");
        lock (_gate)
        {
            Sweep();
            if (_views.Count >= MaxViews || _views.Values.Count(x => x.Owner.Id == owner.Id) >= MaxPerGrant) throw new ApiException(429,"view_limit","Too many active remote views.");
            var view = new ViewIdentity(Guid.NewGuid(),session,label,kind);
            var key = AccessTokens.GenerateToken();
            _views.Add(view.Id,new(view,owner,Digest(key),clock.GetTimestamp()));
            return new(view,key);
        }
    }
    internal ViewIdentity[] List(Guid session)
    {
        lock (_gate) { Sweep(); return _views.Values.Where(e => e.View.SessionId == session).Select(e => e.View).OrderBy(v => v.Id).ToArray(); }
    }
    internal ViewReceipt Publish(Guid session, Guid id, ViewAction[] actions)
    {
        lock (_gate)
        {
            var e = Find(session,id);
            if (actions.Any(a => !ViewActions.Supported(e.View.Kind,a))) throw new ApiException(409,"unsupported_action","This receiver does not support the requested action.");
            if (e.Queue.Count >= MaxBatches) throw new ApiException(429,"view_busy","The receiver has not acknowledged earlier batches.");
            var batch = new ViewBatch(checked(++e.Issued),actions); e.Queue.Enqueue(batch); Signal(e); return new(id,batch.Sequence);
        }
    }
    internal void Close(Guid session, Guid id, string key, AccessGrant owner)
    {
        lock (_gate) { var e = Authenticate(session,id,key,owner); _views.Remove(id); Signal(e); }
    }
    internal async ValueTask<ViewDelivery> ReadAsync(Guid session, Guid id, string key, AccessGrant owner, long after, int ms, CancellationToken token)
    {
        if (after < 0 || ms is < 0 or > 20000) throw new ArgumentException("Invalid cursor or timeout (0–20000 ms).");
        token.ThrowIfCancellationRequested(); Task wait; Entry entry;
        lock (_gate)
        {
            entry = Authenticate(session,id,key,owner);
            if (entry.Reading) throw new ApiException(409,"reader_busy","A receiver poll is already outstanding.");
            if (after < entry.Acknowledged || after > entry.Offered) throw new ApiException(409,"view_cursor","Invalid receiver acknowledgement.");
            entry.Acknowledged = after; entry.Seen = clock.GetTimestamp();
            while (entry.Queue.TryPeek(out var b) && b.Sequence <= after) entry.Queue.Dequeue();
            if (entry.Queue.Count > 0 || ms == 0) return Deliver(entry);
            entry.Reading = true; wait = (entry.Wake ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        try
        {
            try { await wait.WaitAsync(TimeSpan.FromMilliseconds(ms), clock, token).ConfigureAwait(false); } catch (TimeoutException) { }
            token.ThrowIfCancellationRequested();
            lock (_gate) { Authenticate(session,id,key,owner); entry.Seen = clock.GetTimestamp(); return Deliver(entry); }
        }
        finally { lock (_gate) entry.Reading = false; }
    }
    private static ViewDelivery Deliver(Entry e)
    {
        var pending = e.Queue.Take(8).ToArray(); var cursor = pending.Length == 0 ? e.Acknowledged : pending[^1].Sequence;
        e.Offered = Math.Max(e.Offered,cursor); return new(cursor,pending);
    }
    private Entry Authenticate(Guid session,Guid id,string key,AccessGrant owner)
    {
        var e = Find(session,id);
        if (owner.Id != e.Owner.Id || key.Length != 64 || !CryptographicOperations.FixedTimeEquals(Digest(key),e.Secret)) throw new ApiException(403,"view_owner","Invalid receiver proof.");
        return e;
    }
    private Entry Find(Guid session,Guid id)
    {
        Sweep(); return _views.TryGetValue(id,out var e) && e.View.SessionId == session ? e : throw new ApiException(404,"view_gone","No opted-in client with this identity in the session.");
    }
    private void Sweep()
    {
        long now = clock.GetTimestamp();
        foreach (var pair in _views)
            if (!active(pair.Value.Owner) || clock.GetElapsedTime(pair.Value.Seen,now) >= TimeSpan.FromSeconds(60)) { _views.Remove(pair.Key); Signal(pair.Value); }
    }
    private static byte[] Digest(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));
    private static void Signal(Entry e) { e.Wake?.TrySetResult(); e.Wake = null; }
}
