// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Security.Cryptography;
using System.Text;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record AccessGrant(Guid Id, bool IsAdministrator, Guid? SessionId, bool CanControl,
    DateTimeOffset ExpiresUtc)
{
    public bool CanRead(Guid sessionId) => IsAdministrator || SessionId == sessionId;
}

/// <summary>Server-owned capabilities. Only a digest of each sharing token is retained.</summary>
internal sealed class AccessTokens
{
    private readonly byte[] _administratorDigest;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Entry> _shares = [];
    private readonly TimeProvider _time;
    private sealed record Entry(byte[] Digest, AccessGrant Grant);

    public AccessTokens(string administratorToken, TimeProvider? time = null)
    {
        if (administratorToken.Length < 32 || administratorToken.Length > 512)
            throw new ArgumentException("The administrator token must contain 32 to 512 characters.", nameof(administratorToken));
        _administratorDigest = Digest(administratorToken);
        _time = time ?? TimeProvider.System;
    }

    public static string GenerateToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public AccessGrant? Resolve(string? token)
    {
        if (token is null || token.Length < 32 || token.Length > 512) return null;
        byte[] digest = Digest(token);
        if (CryptographicOperations.FixedTimeEquals(digest, _administratorDigest))
            return new(Guid.Empty, true, null, true, DateTimeOffset.MaxValue);
        lock (_gate)
        {
            PurgeExpired();
            foreach (Entry entry in _shares.Values)
                if (CryptographicOperations.FixedTimeEquals(digest, entry.Digest)) return entry.Grant;
        }
        return null;
    }

    public bool IsActive(AccessGrant grant)
    {
        if (grant.IsAdministrator) return true;
        lock (_gate)
            return grant.ExpiresUtc > _time.GetUtcNow() && _shares.ContainsKey(grant.Id);
    }

    public ShareCreated Create(Guid sessionId, ShareRequest request)
    {
        if (request.Role is not ("observer" or "controller")) throw new ArgumentException("Role must be observer or controller.");
        if (request.LifetimeMinutes is < 1 or > 1440) throw new ArgumentException("Share lifetime must be 1 to 1440 minutes.");
        string token = GenerateToken();
        Guid id = Guid.NewGuid();
        DateTimeOffset expires = _time.GetUtcNow().AddMinutes(request.LifetimeMinutes);
        AccessGrant grant = new(id, false, sessionId, request.Role == "controller", expires);
        lock (_gate)
        {
            PurgeExpired();
            if (_shares.Count >= 256) throw new ArgumentException("The server share limit has been reached.");
            _shares.Add(id, new(Digest(token), grant));
        }
        return new(id, sessionId, request.Role, expires, token);
    }

    public ShareList List(Guid sessionId)
    {
        lock (_gate)
        {
            PurgeExpired();
            return new(_shares.Values.Where(x => x.Grant.SessionId == sessionId)
                .Select(x => new ShareDescriptor(x.Grant.Id, sessionId, x.Grant.CanControl ? "controller" : "observer", x.Grant.ExpiresUtc)).ToArray());
        }
    }

    public bool Revoke(Guid sessionId, Guid id)
    {
        lock (_gate)
            return _shares.TryGetValue(id, out Entry? entry) && entry.Grant.SessionId == sessionId && _shares.Remove(id);
    }

    private void PurgeExpired()
    {
        DateTimeOffset now = _time.GetUtcNow();
        foreach (Guid id in _shares.Where(x => x.Value.Grant.ExpiresUtc <= now).Select(x => x.Key).ToArray()) _shares.Remove(id);
    }

    private static byte[] Digest(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
