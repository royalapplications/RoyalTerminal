// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Net;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record PushSettings(bool Enabled,string? ApplicationServerKey);
internal sealed record PushRequest(Guid SessionId,Guid BlockId,string Endpoint);
internal sealed record PushReceipt(Guid Id,DateTimeOffset ExpiresUtc);

/// <summary>Small, in-memory watches scoped to a live block, OS-push endpoint, and existing grant. No VT replay, no push payload.</summary>
internal sealed class WorkspacePush(WorkspaceOperations workspaces,AccessTokens tokens,VapidSender sender,TimeProvider clock):BackgroundService
{
    private sealed class Watch(Guid id, Guid block,AccessGrant grant,Uri endpoint,TerminalMetadataFeed feed,long cursor,DateTimeOffset expires)
    {
        internal readonly Guid Id=id,Block=block; internal readonly AccessGrant Grant=grant;internal readonly Uri Endpoint=endpoint;internal readonly TerminalMetadataFeed Feed=feed;
        internal readonly DateTimeOffset Expires=expires;internal long Cursor=cursor;internal DateTimeOffset NextSend; internal bool Pending;
    }
    private readonly object _gate=new();private readonly Dictionary<Guid,Watch> _watches=[];
    internal string PublicKey=>sender.PublicKey;
    internal async Task<PushReceipt> SubscribeAsync(PushRequest request,AccessGrant grant,CancellationToken ct)
    {
        ServerHttp.RequireSession(grant,request.SessionId);
        if(!tokens.IsActive(grant))throw new ApiException(403,"expired","The workspace grant expired.");
        var uri=VapidSender.Validate(request.Endpoint??string.Empty);
        var feed=workspaces.Inspection(request.SessionId,request.BlockId).Metadata;
        var now=await feed.ReadAsync(cancellationToken:ct);
        if(now.Ended)throw new ApiException(409,"ended","The block has ended.");
        lock(_gate)
        {
            if(!tokens.IsActive(grant)) throw new ApiException(403,"expired","The workspace grant expired.");
            var existing=_watches.Values.FirstOrDefault(w=>w.Grant.Id==grant.Id && w.Block==request.BlockId && w.Endpoint==uri);
            if(existing!=null)return new(existing.Id,existing.Expires);
            if(_watches.Count>=64 || _watches.Values.Count(w=>w.Grant.Id==grant.Id)>=8)throw new ApiException(429,"push_capacity","Too many device watches.");
            var expires=clock.GetUtcNow().AddHours(24);if(grant.ExpiresUtc<expires)expires=grant.ExpiresUtc;
            var w=new Watch(Guid.NewGuid(),request.BlockId,grant,uri,feed,now.NextSequence,expires);_watches.Add(w.Id,w);return new(w.Id,expires);
        }
    }
    internal void Remove(Guid id,AccessGrant grant){lock(_gate){if(_watches.TryGetValue(id,out var w)&&(w.Grant.Id==grant.Id || grant.IsAdministrator))_watches.Remove(id);}}
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var tick=new PeriodicTimer(TimeSpan.FromSeconds(1),clock);
        while(await tick.WaitForNextTickAsync(stoppingToken)) await PollAsync(stoppingToken);
    }
    internal async Task PollAsync(CancellationToken ct)
    {
        Watch[] watches;lock(_gate)watches=[.._watches.Values];
        await Parallel.ForEachAsync(watches,new ParallelOptions{MaxDegreeOfParallelism=8,CancellationToken=ct},async(w,token)=>
        {
            bool Active(){lock(_gate)return _watches.ContainsKey(w.Id)&&w.Expires>clock.GetUtcNow()&&tokens.IsActive(w.Grant);}
            void Drop(){lock(_gate)_watches.Remove(w.Id);}
            if(!Active()){Drop();return;}
            try
            {
                var page=await w.Feed.ReadAsync(w.Cursor,128,cancellationToken:token); w.Cursor=page.NextSequence;
                w.Pending |= page.Events.Any(e=>e.Name is "terminal.bell" or "terminal.desktop_notification" || e.Name=="terminal.program_status_changed" && e.Record?.State is TerminalProgramStatusState.Blocked or TerminalProgramStatusState.Error || e.Name=="terminal.child_exited"&&e.ExitCode!=0);
                if(w.Pending && w.NextSend<=clock.GetUtcNow() && Active())
                {
                    w.Pending=false; w.NextSend=clock.GetUtcNow().AddSeconds(30);
                    var status=await sender.SendAsync(w.Endpoint,clock,token);
                    if((int)status is not (>=200 and <=299) && status!=HttpStatusCode.TooManyRequests && (int)status<500){Drop();return;}
                    if(status==HttpStatusCode.TooManyRequests || (int)status>=500)w.NextSend=clock.GetUtcNow().AddMinutes(5);
                }
                if(page.Ended&&!w.Pending)Drop();
            }
            catch(MultiplexerException){Drop();} // Never convert an evicted metadata prefix into a fresh notification.
            catch(HttpRequestException){w.NextSend=clock.GetUtcNow().AddMinutes(5);}
            catch(OperationCanceledException) when(!token.IsCancellationRequested){w.NextSend=clock.GetUtcNow().AddMinutes(5);}
        });
    }
}
