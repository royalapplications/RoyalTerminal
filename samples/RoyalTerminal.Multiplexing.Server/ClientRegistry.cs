// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed class ClientRegistry
{
    private sealed record Entry(ConnectedClient Descriptor, AccessGrant Grant, CancellationTokenSource Lifetime);
    private readonly ConcurrentDictionary<Guid, Entry> _clients = new();

    public void Add(Guid id, Guid sessionId, Guid blockId, AccessGrant grant, bool controller, CancellationTokenSource lifetime)
        => _clients[id] = new(new(id, sessionId, blockId, controller ? "controller" : "observer", DateTimeOffset.UtcNow), grant, lifetime);

    public void Remove(Guid id) => _clients.TryRemove(id, out _);

    public ClientList List(AccessGrant grant) => new(_clients.Values
        .Where(x => grant.CanRead(x.Descriptor.SessionId)).Select(x => x.Descriptor).ToArray());

    public bool Disconnect(Guid id)
    {
        if (!_clients.TryGetValue(id, out Entry? entry)) return false;
        try { entry.Lifetime.Cancel(); } catch (ObjectDisposedException) { }
        return true;
    }

    public void Revoke(Guid grantId)
    {
        foreach ((Guid id, Entry entry) in _clients)
            if (entry.Grant.Id == grantId) Disconnect(id);
    }
}
