// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

/// <summary>
/// An owned restored terminal and its persistent parser. The caller serializes processor,
/// screen and incremental decoder operations, just as for an ordinary managed terminal.
/// Snapshot decoding never registers external callbacks or changes OS secure-input state.
/// </summary>
public sealed class ManagedTerminalSnapshot : IDisposable
{
    internal ManagedTerminalSnapshot(TerminalScreen screen, BasicVtProcessor processor,
        ulong primaryHistoryRows, ulong alternateHistoryRows)
    {
        Screen = screen;
        Processor = processor;
        PrimaryHistoryRows = primaryHistoryRows;
        AlternateHistoryRows = alternateHistoryRows;
    }

    /// <summary>The stable published screen; synchronized output may stage changes privately.</summary>
    public TerminalScreen Screen { get; }

    /// <summary>The restored persistent processor, ready for bytes following the snapshot cut.</summary>
    public BasicVtProcessor Processor { get; }

    /// <summary>Advisory source primary history extent; not the count of locally retained rows.</summary>
    public ulong PrimaryHistoryRows { get; }

    /// <summary>Advisory source alternate history extent; zero when that screen is absent.</summary>
    public ulong AlternateHistoryRows { get; }

    internal bool IsDisposed { get; private set; }

    /// <summary>Restores one complete snapshot, rejecting trailing bytes. No partial result escapes on failure.</summary>
    public static ManagedTerminalSnapshot Restore(ReadOnlyMemory<byte> source, ManagedTerminalSnapshotOptions? options = null)
    {
        using ManagedTerminalSnapshotDecoder decoder = new(source, options);
        ManagedTerminalSnapshot result = decoder.Ready();
        try
        {
            while (decoder.Next() is not null) { }
            if (decoder.SourceOffset != source.Length) throw new InvalidDataException("Trailing bytes after snapshot FINISH.");
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    /// <summary>
    /// Restores through FINISH without closing the caller's stream or reading subsequent transport
    /// bytes. No partial result escapes on failure. The stream need not support seeking.
    /// </summary>
    public static ManagedTerminalSnapshot Restore(Stream source, ManagedTerminalSnapshotOptions? options = null)
    {
        using ManagedTerminalSnapshotDecoder decoder = new(source, options);
        ManagedTerminalSnapshot result = decoder.Ready();
        try
        {
            while (decoder.Next() is not null) { }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    /// <summary>Ends processor rendering holds; does not close the snapshot source or a PTY.</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Processor.Dispose();
    }
}
