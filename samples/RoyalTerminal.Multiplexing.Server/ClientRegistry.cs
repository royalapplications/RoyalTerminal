// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Collections.Concurrent;
using System.Text.Json;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed class ClientRegistry
{
    private sealed record Entry(ConnectedClient Descriptor, AccessGrant Grant, CancellationTokenSource Lifetime);
    private readonly ConcurrentDictionary<Guid,Entry> _clients = new();
    private readonly object _changes = new();
    internal WorkspaceActivityHub? Activity { get; set; }

    public void Add(Guid id, Guid sessionId, Guid blockId, AccessGrant grant, bool controller, CancellationTokenSource lifetime)
    {
        var client = new ConnectedClient(id, sessionId, blockId, controller ? "controller" : "observer", DateTimeOffset.UtcNow);
        lock (_changes)
        {
            if (!_clients.TryAdd(id,new(client,grant,lifetime))) throw new InvalidOperationException("An attachment identity was registered twice.");
            Publish(true,client,grant);
        }
    }
    public void Remove(Guid id)
    {
        lock (_changes) if (_clients.TryRemove(id,out var e)) Publish(false,e.Descriptor,e.Grant);
    }
    private void Publish(bool connected,ConnectedClient client,AccessGrant grant)
    {
        var hub=Activity; if(hub is null)return;
        // A capability role is not a personally identifying OS or Tailscale login.
        var detail=JsonSerializer.SerializeToElement(new WorkspaceClientEvent(grant.IsAdministrator?"administrator":"session-share",client.Role,client.ConnectedUtc),WorkspaceActivityJson.Default.WorkspaceClientEvent);
        hub.RecordClient(connected,client.SessionId,client.BlockId,client.Id,detail);
    }
    public ClientList List(AccessGrant grant) => new(_clients.Values.Where(x=>grant.CanRead(x.Descriptor.SessionId)).Select(x=>x.Descriptor).ToArray());
    public bool Disconnect(Guid id)
    {
        if (!_clients.TryGetValue(id,out var e)) return false;
        try { e.Lifetime.Cancel(); } catch(ObjectDisposedException) { }
        return true;
    }
    public void Revoke(Guid grantId)
    {
        foreach ((Guid id,Entry e) in _clients) if(e.Grant.Id==grantId) Disconnect(id);
    }
}
