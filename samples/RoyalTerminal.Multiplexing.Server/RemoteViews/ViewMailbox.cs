// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Security.Cryptography;
using System.Text;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record ViewIdentity(Guid Id, Guid SessionId, string Label, string Kind, int NavigationVersion = 1);
internal sealed record ViewLease(ViewIdentity View, string ReaderKey) { public int CompletionProtocol => 1; }
internal sealed record ViewAction(string Name, Guid? BlockId = null, Guid? WindowId = null, string? Direction = null);
internal sealed record ViewBatch(long Sequence, ViewAction[] Actions);
internal sealed record ViewDelivery(long Cursor, ViewBatch[] Batches);
internal sealed record ViewReceipt(Guid ClientId, long Sequence);
internal sealed record ViewOutcome(Guid ClientId, long Sequence, string State, int Total, int? Applied);

/// <summary>All delivery and reporting transitions share one lock. Waiters never run inline on the publishing thread.</summary>
internal sealed class ViewMailbox(TimeProvider clock, Func<AccessGrant,bool> active)
{
    private const int MaxViews=128, MaxPerGrant=8, MaxBatches=32, ResultHistory=64, MaxWaiters=16;
    private readonly Lock _gate=new();
    private readonly Dictionary<Guid,Entry> _views=[];
    private sealed class Entry(ViewIdentity view,AccessGrant owner,byte[] secret,long now)
    {
        internal readonly ViewIdentity View=view;
        internal readonly AccessGrant Owner=owner;
        internal readonly byte[] Secret=secret;
        internal long Seen=now, Issued, Offered, Acknowledged;
        internal readonly Queue<ViewBatch> Queue=[];
        internal readonly Dictionary<long,ViewOutcome> Results=[];
        internal readonly Dictionary<long,TaskCompletionSource> CompletionSignals=[];
        internal int Waiters;
        internal bool Reading;
        internal TaskCompletionSource? Wake;
    }
    internal ViewLease Open(Guid session,string label,string kind,AccessGrant owner,int navigationVersion=1)
    {
        label=label.Trim();if(label.Length is 0 or >80||label.Any(char.IsControl))throw new ArgumentException("Invalid client label.");
        _=ViewCapabilities.Forms(kind,navigationVersion);
        lock(_gate)
        {
            Sweep();if(_views.Count>=MaxViews||_views.Values.Count(x=>x.Owner.Id==owner.Id)>=MaxPerGrant)throw new ApiException(429,"view_limit","Too many active remote views.");
            var view=new ViewIdentity(Guid.NewGuid(),session,label,kind,navigationVersion);var key=AccessTokens.GenerateToken();
            _views.Add(view.Id,new(view,owner,Digest(key),clock.GetTimestamp()));return new(view,key);
        }
    }
    internal ViewIdentity[] List(Guid session)
    {lock(_gate){Sweep();return _views.Values.Where(e=>e.View.SessionId==session).Select(e=>e.View).OrderBy(v=>v.Id).ToArray();}}
    internal ViewActionCatalog Describe(Guid session,Guid id)
    {lock(_gate){var v=Find(session,id).View;return new(id,session,v.Kind,v.NavigationVersion,ViewCapabilities.Forms(v.Kind,v.NavigationVersion));}}
    internal ViewReceipt Publish(Guid session,Guid id,ViewAction[] actions)
    {
        lock(_gate)
        {
            var e=Find(session,id);
            if(actions.Length is <1 or >16||actions.Any(a=>!ViewCapabilities.Allows(e.View.Kind,e.View.NavigationVersion,a)))throw new ApiException(409,"unsupported_action","Unsupported action batch.");
            if(e.Queue.Count>=MaxBatches)throw new ApiException(429,"view_busy","The receiver has not acknowledged earlier batches.");
            var batch=new ViewBatch(checked(++e.Issued),[..actions]);e.Queue.Enqueue(batch);Signal(e);return new(id,batch.Sequence);
        }
    }
    internal void Close(Guid session,Guid id,string key,AccessGrant owner)
    {lock(_gate){var e=Authenticate(session,id,key,owner);_views.Remove(id);Withdraw(e);}}
    internal async ValueTask<ViewDelivery> ReadAsync(Guid session,Guid id,string key,AccessGrant owner,long after,int ms,CancellationToken token)
    {
        if(after<0||ms is <0 or >20000)throw new ArgumentException("Invalid cursor or timeout (0–20000 ms).");
        token.ThrowIfCancellationRequested();Task wait;Entry entry;
        lock(_gate)
        {
            entry=Authenticate(session,id,key,owner);
            if(entry.Reading)throw new ApiException(409,"reader_busy","A receiver poll is already outstanding.");
            if(after<entry.Acknowledged||after>entry.Offered)throw new ApiException(409,"view_cursor","Invalid receiver acknowledgement.");
            entry.Seen=clock.GetTimestamp();
            // Legacy receivers advance a delivery cursor; this does not tell us whether the presentation succeeded.
            while(entry.Queue.TryPeek(out var batch)&&batch.Sequence<=after)Complete(entry,batch,null);
            if(entry.Queue.Count>0||ms==0)return Deliver(entry);
            entry.Reading=true;wait=(entry.Wake??=NewSignal()).Task;
        }
        try
        {
            try{await wait.WaitAsync(TimeSpan.FromMilliseconds(ms),clock,token).ConfigureAwait(false);}catch(TimeoutException){}
            token.ThrowIfCancellationRequested();lock(_gate){Authenticate(session,id,key,owner);entry.Seen=clock.GetTimestamp();return Deliver(entry);}
        }
        finally{lock(_gate)entry.Reading=false;}
    }
    internal ViewOutcome Finish(Guid session,Guid id,string key,AccessGrant owner,long sequence,int applied)
    {
        lock(_gate)
        {
            var e=Authenticate(session,id,key,owner);
            if(e.Results.TryGetValue(sequence,out var old)&&old.Applied==applied)return old;
            if(e.Reading||!e.Queue.TryPeek(out var batch)||batch.Sequence!=sequence||sequence>e.Offered)throw new ApiException(409,"view_cursor","Report the next offered batch in order.");
            if(applied<0||applied>batch.Actions.Length)throw new ArgumentException("Applied count exceeds the batch bounds.");
            e.Seen=clock.GetTimestamp();return Complete(e,batch,applied);
        }
    }
    private static ViewOutcome Complete(Entry e,ViewBatch batch,int? applied)
    {
        e.Queue.Dequeue();e.Acknowledged=batch.Sequence;
        var outcome=new ViewOutcome(e.View.Id,batch.Sequence,applied is null?"unreported":applied==batch.Actions.Length?"applied":"declined",batch.Actions.Length,applied);
        e.Results.Add(batch.Sequence,outcome);e.Results.Remove(batch.Sequence-ResultHistory);
        if(e.CompletionSignals.Remove(batch.Sequence,out var signal))signal.TrySetResult();return outcome;
    }
    internal async ValueTask<ViewOutcome> ResultAsync(Guid session,Guid id,long sequence,int ms,CancellationToken ct)
    {
        if(sequence<1||ms is <0 or >20000)throw new ArgumentException("Invalid sequence or timeout (0–20000 ms).");
        ct.ThrowIfCancellationRequested();Task wait;Entry e;
        lock(_gate)
        {
            e=Find(session,id);var initial=Inspect(e,sequence);
            if(ms==0||initial.State is not ("queued" or "offered"))return initial;
            if(e.Waiters>=MaxWaiters)throw new ApiException(429,"view_waiters","Too many simultaneous result readers.");
            if(!e.CompletionSignals.TryGetValue(sequence,out var signal))e.CompletionSignals.Add(sequence,signal=NewSignal());
            ++e.Waiters;wait=signal.Task;
        }
        try
        {
            try{await wait.WaitAsync(TimeSpan.FromMilliseconds(ms),clock,ct).ConfigureAwait(false);}catch(TimeoutException){}
            ct.ThrowIfCancellationRequested();lock(_gate)return Inspect(Find(session,id),sequence);
        }
        finally{lock(_gate)--e.Waiters;}
    }
    private static ViewOutcome Inspect(Entry e,long sequence)
    {
        if(e.Results.TryGetValue(sequence,out var done))return done;
        if(sequence<=e.Acknowledged)throw new ApiException(410,"result_expired","The receiver result is no longer retained.");
        var batch=e.Queue.FirstOrDefault(b=>b.Sequence==sequence)??throw new ApiException(404,"unknown_receipt","No such receiver sequence.");
        return new(e.View.Id,sequence,sequence<=e.Offered?"offered":"queued",batch.Actions.Length,null);
    }
    private static ViewDelivery Deliver(Entry e)
    {var pending=e.Queue.Take(8).ToArray();var cursor=pending.Length==0?e.Acknowledged:pending[^1].Sequence;e.Offered=Math.Max(e.Offered,cursor);return new(cursor,pending);}
    private Entry Authenticate(Guid session,Guid id,string key,AccessGrant owner)
    {
        var e=Find(session,id);
        if(owner.Id!=e.Owner.Id||key.Length!=64||!CryptographicOperations.FixedTimeEquals(Digest(key),e.Secret))throw new ApiException(403,"view_owner","Invalid receiver proof.");
        return e;
    }
    private Entry Find(Guid session,Guid id){Sweep();return _views.TryGetValue(id,out var e)&&e.View.SessionId==session?e:throw new ApiException(404,"view_gone","No opted-in client with this identity in the session.");}
    private void Sweep()
    {
        long now=clock.GetTimestamp();foreach(var pair in _views)if(!active(pair.Value.Owner)||clock.GetElapsedTime(pair.Value.Seen,now)>=TimeSpan.FromSeconds(60)){_views.Remove(pair.Key);Withdraw(pair.Value);}
    }
    private static TaskCompletionSource NewSignal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static byte[] Digest(string key)=>SHA256.HashData(Encoding.UTF8.GetBytes(key));
    private static void Signal(Entry e){e.Wake?.TrySetResult();e.Wake=null;}
    private static void Withdraw(Entry e){Signal(e);foreach(var s in e.CompletionSignals.Values)s.TrySetResult();e.CompletionSignals.Clear();}
}
