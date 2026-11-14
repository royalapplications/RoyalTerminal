// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Net;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record PushSettings(bool Enabled, string? ApplicationServerKey);
internal sealed record PushRequest(Guid SessionId, Guid BlockId, string Endpoint);
internal sealed record PushReceipt(Guid Id, DateTimeOffset ExpiresUtc);

/// <summary>Session-authorized, memory-only watches. Push messages contain no payload.</summary>
internal sealed class WorkspacePush(WorkspaceOperations workspaces, AccessTokens tokens, VapidSender sender, TimeProvider clock) : BackgroundService
{
    private sealed class Watch(Guid block, AccessGrant grant, Uri endpoint, TerminalMetadataFeed feed, long cursor, DateTimeOffset expires)
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal Guid Block { get; } = block;
        internal AccessGrant Grant { get; } = grant;
        internal Uri Endpoint { get; } = endpoint;
        internal TerminalMetadataFeed Feed { get; } = feed;
        internal DateTimeOffset Expires { get; } = expires;
        internal long Cursor = cursor;
        internal DateTimeOffset NextSend;
        internal int Failures;
        internal bool Pending;
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Watch> _watches = [];
    private int _polling;
    internal string PublicKey => sender.PublicKey;

    internal async Task<PushReceipt> SubscribeAsync(PushRequest request, AccessGrant grant, CancellationToken ct)
    {
        ServerHttp.RequireSession(grant, request.SessionId);
        if (!tokens.IsActive(grant)) throw new ApiException(403, "expired", "The workspace grant expired.");
        Uri uri = VapidSender.Validate(request.Endpoint ?? string.Empty);
        var feed = workspaces.Inspection(request.SessionId, request.BlockId).Metadata;
        var page = await feed.ReadAsync(cancellationToken: ct);
        if (page.Ended) throw new ApiException(409, "ended", "The block has ended.");
        lock (_gate)
        {
            if (!tokens.IsActive(grant)) throw new ApiException(403, "expired", "The workspace grant expired.");
            var existing = _watches.Values.FirstOrDefault(w => w.Grant.Id == grant.Id && w.Block == request.BlockId && w.Endpoint == uri);
            if (existing is not null) return new(existing.Id, existing.Expires);
            if (_watches.Count >= 64 || _watches.Values.Count(w => w.Grant.Id == grant.Id) >= 8)
                throw new ApiException(429, "push_capacity", "Too many device watches.");
            var expires = clock.GetUtcNow().AddHours(24);
            if (grant.ExpiresUtc < expires) expires = grant.ExpiresUtc;
            var watch = new Watch(request.BlockId, grant, uri, feed, page.NextSequence, expires);
            _watches.Add(watch.Id, watch);
            return new(watch.Id, watch.Expires);
        }
    }

    internal void Remove(Guid id, AccessGrant grant)
    {
        lock (_gate) if (_watches.TryGetValue(id, out var watch) && (grant.IsAdministrator || watch.Grant.Id == grant.Id)) _watches.Remove(id);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        while (await tick.WaitForNextTickAsync(stoppingToken)) await PollAsync(stoppingToken);
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            Watch[] snapshot;
            lock (_gate) snapshot = [.. _watches.Values];
            await Parallel.ForEachAsync(snapshot, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, PollWatchAsync);
        }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private bool IsActive(Watch w)
    {
        lock (_gate) return _watches.ContainsKey(w.Id) && w.Expires > clock.GetUtcNow() && tokens.IsActive(w.Grant);
    }

    private void Drop(Watch w) { lock (_gate) _watches.Remove(w.Id); }

    private async ValueTask PollWatchAsync(Watch w, CancellationToken ct)
    {
        if (!IsActive(w)) { Drop(w); return; }
        TerminalMetadataPage page;
        try { page = await w.Feed.ReadAsync(w.Cursor, 128, cancellationToken: ct); }
        catch (MultiplexerException) { Drop(w); return; } // Never guess which evicted events required attention.
        w.Cursor = page.NextSequence;
        w.Pending |= page.Events.Any(NeedsAttention);
        if (w.Pending && w.NextSend <= clock.GetUtcNow())
        {
            if (!IsActive(w)) { Drop(w); return; }
            HttpStatusCode status;
            try { status = await sender.SendAsync(w.Endpoint, clock, ct); }
            catch (HttpRequestException) { Retry(w); return; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { Retry(w); return; }
            if ((int)status is >= 200 and < 300)
            {
                w.Pending = false;
                w.Failures = 0;
                w.NextSend = clock.GetUtcNow().AddSeconds(30);
            }
            else if (status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500)
            { Retry(w); return; }
            else { Drop(w); return; }
        }
        // End-of-stream must not erase a pending alert for the child's exit.
        if (page.Ended && !w.Pending) Drop(w);
    }

    private void Retry(Watch w)
    {
        if (++w.Failures >= 6) { Drop(w); return; }
        w.NextSend = clock.GetUtcNow().AddSeconds(10 << (w.Failures - 1));
    }

    private static bool NeedsAttention(TerminalMetadataEvent e) => e.Name switch
    {
        "terminal.bell" or "terminal.desktop_notification" => true,
        "terminal.program_status_changed" => e.Record?.State is TerminalProgramStatusState.Blocked or TerminalProgramStatusState.Error,
        "terminal.child_exited" => e.ExitCode is not null and not 0,
        _ => false,
    };
}
