// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>One ordered metadata feed for every admitted process in a session, across windows and placements.</summary>
public sealed class SessionTerminalJournal
{
    private readonly TerminalMetadataFeed _journal = new();

    /// <summary>Atomically incorporates a successful launch's retained startup records and subscribes to its live changes.</summary>
    public void Include(Guid blockId, TerminalMetadataFeed source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (blockId == Guid.Empty) throw new ArgumentException("A block identity is required.", nameof(blockId));
        source.AdmitTo(_journal, blockId);
    }

    /// <summary>Reads session records, each carrying its source block ID and local cursor.</summary>
    public ValueTask<TerminalMetadataPage> ReadAsync(long? after = null, int limit = 64, int timeoutMs = 0, CancellationToken cancellationToken = default)
        => _journal.ReadAsync(after, limit, timeoutMs, cancellationToken);

    /// <summary>Wakes waiters on session closure. An individual process exit never completes the session stream.</summary>
    public void Complete() => _journal.End();
}
