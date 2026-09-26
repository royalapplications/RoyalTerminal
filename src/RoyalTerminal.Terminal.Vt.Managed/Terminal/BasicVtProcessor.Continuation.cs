// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    /// <summary>Whether both the VT parser and UTF-8 decoder have no unfinished input.</summary>
    public bool IsParserGround => _state == ParserState.Ground && _utf8Remaining == 0;

    /// <summary>
    /// Processes only the prefix needed to finish the pending VT sequence or UTF-8 scalar.
    /// Returns true when ground was reached, and reports consumed bytes (zero if already
    /// at ground). On false, all supplied bytes were consumed and more input is needed.
    /// </summary>
    public bool ProcessUntilGround(ReadOnlySpan<byte> data, out int consumed)
    {
        ProcessCore(data, stopAtGround: true, out consumed);
        return IsParserGround;
    }

    /// <summary>
    /// Copies the minimal unfinished input that recreates parser state when replayed on
    /// a ground-state processor with equivalent terminal state. The caller owns the array.
    /// Throws if retention is disabled or its configured limit has been exceeded.
    /// </summary>
    public byte[] GetContinuation()
    {
        _screen.ThrowIfSnapshotMutationFailed();
        return _continuation.GetBytes().ToArray();
    }

    /// <summary>Writes the unfinished parser input without allocating an intermediate array.</summary>
    public void WriteContinuationTo(Stream destination)
    {
        _screen.ThrowIfSnapshotMutationFailed();
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination is not writable.", nameof(destination));
        destination.Write(_continuation.GetBytes());
    }
}
