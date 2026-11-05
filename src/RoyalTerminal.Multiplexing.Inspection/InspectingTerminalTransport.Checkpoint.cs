// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>Immutable logical cut. The caller owns the payload; each capture has independent storage.</summary>
public sealed record MultiplexerCheckpoint(MultiplexerBlockDescriptor Block, ReadOnlyMemory<byte> Binary);

public sealed partial class InspectingTerminalTransport
{
    /// <summary>Eight MiB maximum serialized workspace checkpoint including framing.</summary>
    public const int MaximumCheckpointBytes = 8 * 1024 * 1024;

    /// <summary>Captures both screens and the unfinished parser operation, along with the descriptor cursor
    /// for this inspected block. The host must supply its trusted, synchronous descriptor lookup for this exact block;
    /// the lookup must not do network I/O or take a process-operation semaphore.</summary>
    public MultiplexerCheckpoint CaptureCheckpoint(Func<MultiplexerBlockDescriptor> describe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(describe);
        lock (_gate)
        {
            ThrowIfDisposed(); cancellationToken.ThrowIfCancellationRequested();
            if (!_started && !_exited) throw new MultiplexerException(MultiplexerErrorCode.Conflict, "The terminal is not ready to checkpoint.");
            // DataReceived/DimensionsChanged/ProcessExited publish synchronously under this same gate.
            // The broker can split an output callback across several sequences: use its real cursor, not a second counter.
            MultiplexerBlockDescriptor cut = describe();
            if (cut.State is MultiplexerBlockState.Starting or MultiplexerBlockState.Closed || cut.Dimensions != _dimensions)
                throw new MultiplexerException(MultiplexerErrorCode.Conflict, "The block does not have a stable checkpoint cut.");
            _processor.RefreshTimedState();
            using CappedSnapshotStream output = new(MaximumCheckpointBytes, cancellationToken);
            _processor.WriteBinarySnapshotTo(output, WorkspaceCheckpointLimits());
            cancellationToken.ThrowIfCancellationRequested();
            return new(cut, output.GetBuffer().AsMemory(0, checked((int)output.Length)));
        }
    }

    /// <summary>Decoder limits for checkpoints from this host. Never accept resource limits from a remote sender.</summary>
    public static GhosttySnapshotDecodeLimits WorkspaceCheckpointLimits() => new(
        MaximumPayloadBytes: 4 * 1024 * 1024, MaximumTotalPayloadBytes: MaximumCheckpointBytes,
        MaximumCells: 1024 * 1024, MaximumPages: 4096, MaximumSuffixCodepointsPerPage: 256 * 1024,
        MaximumStringBytesPerRecord: 256 * 1024, MaximumContinuationBytes: 64 * 1024);

    private sealed class CappedSnapshotStream(int limit, CancellationToken token) : MemoryStream
    {
        private void Check(int count)
        {
            token.ThrowIfCancellationRequested();
            if (count < 0 || Position > limit - count) throw new InvalidDataException("The checkpoint exceeds its transfer budget.");
        }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
    }
}
