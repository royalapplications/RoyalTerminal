// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Multiplexing;

/// <summary>Explicit bounds for the experimental broker. They do not limit process-side memory.</summary>
public sealed record MultiplexerOptions
{
    /// <summary>Maximum live sessions.</summary>
    public int MaxSessions { get; init; } = 32;
    /// <summary>Maximum blocks, including exited blocks, per session.</summary>
    public int MaxBlocksPerSession { get; init; } = 32;
    /// <summary>Maximum windows per session.</summary>
    public int MaxWindowsPerSession { get; init; } = 16;
    /// <summary>Maximum simultaneous attachments to one block.</summary>
    public int MaxAttachmentsPerBlock { get; init; } = 8;
    /// <summary>Maximum output bytes in the hot replay journal per block.</summary>
    public int MaxJournalBytes { get; init; } = 8 * 1024 * 1024;
    /// <summary>Maximum retained hot events, including non-output events.</summary>
    public int MaxJournalEvents { get; init; } = 4096;
    /// <summary>Optional additional cold history budget per block. Zero disables it. Payload pages are losslessly
    /// compressed; every event incurs a logical metadata charge. Older pages are evicted first.</summary>
    public int MaxColdReplayBytes { get; init; }
    /// <summary>Maximum output bytes in a single event. Larger transport chunks are split.</summary>
    public int MaxEventBytes { get; init; } = 16 * 1024;
    /// <summary>Maximum events queued for an attachment's hot replay and live output.</summary>
    public int MaxPendingEventsPerAttachment { get; init; } = 8192;
    /// <summary>Maximum output bytes queued for an attachment's hot replay and live output.</summary>
    public int MaxPendingBytesPerAttachment { get; init; } = 16 * 1024 * 1024;
    /// <summary>Maximum accepted input bytes per operation.</summary>
    public int MaxInputBytes { get; init; } = 64 * 1024;
    /// <summary>Maximum rows or columns accepted from clients.</summary>
    public int MaxDimension { get; init; } = 4096;
    /// <summary>Maximum label length in UTF-16 code units.</summary>
    public int MaxLabelLength { get; init; } = 256;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxSessions, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxBlocksPerSession, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxWindowsPerSession, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxAttachmentsPerBlock, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxJournalBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxJournalEvents, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxColdReplayBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxColdReplayBytes, 32 * 1024 * 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxEventBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxEventBytes, MaxJournalBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPendingEventsPerAttachment, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPendingBytesPerAttachment, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxInputBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxDimension, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxLabelLength, 1);
    }
}
