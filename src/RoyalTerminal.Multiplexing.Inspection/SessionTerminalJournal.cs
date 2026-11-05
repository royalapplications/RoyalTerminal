// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>One ordered metadata feed for every admitted process in a session, across windows and placements.</summary>
public sealed class SessionTerminalJournal
{
    private readonly TerminalMetadataFeed _journal;
    /// <summary>Creates a standalone metadata feed.</summary>
    public SessionTerminalJournal() => _journal = new();
    /// <summary>Forwards the same admitted prefix and live events into mixed workspace activity without polling or holding a control lease.</summary>
    public SessionTerminalJournal(WorkspaceActivityHub activity,Guid sessionId) => _journal = new(activity,sessionId);
    /// <summary>Atomically incorporates retained startup records and subscribes to live changes.</summary>
    public void Include(Guid blockId, TerminalMetadataFeed source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (blockId == Guid.Empty) throw new ArgumentException("A block identity is required.",nameof(blockId));
        source.AdmitTo(_journal,blockId);
    }
    /// <summary>Reads session records carrying source block identity and local cursor.</summary>
    public ValueTask<TerminalMetadataPage> ReadAsync(long? after=null,int limit=64,int timeoutMs=0,CancellationToken cancellationToken=default)
        => _journal.ReadAsync(after,limit,timeoutMs,cancellationToken);
    /// <summary>Wakes waiters on session closure; process exit does not end the session stream.</summary>
    public void Complete() => _journal.End();
}
