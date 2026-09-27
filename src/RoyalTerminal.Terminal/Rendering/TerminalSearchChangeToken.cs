// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Lazy search invalidation shared by a live row buffer and its rows. Contains
/// no owner references, so retaining a capture cannot root a live screen/history.
/// Reads and writes require the owning screen lock, like row mutations themselves.
/// </summary>
internal sealed class TerminalSearchChangeToken(ulong revision = 0, int activeStart = 0)
{
    private ulong _capturedRevision = ulong.MaxValue;
    private bool _historyChanged = true;

    internal ulong Revision { get; private set; } = revision;
    internal int ActiveStart { get; } = activeStart;
    internal bool RequiresRebinding { get; private set; }

    // Saturation permanently disables reuse instead of letting a wrapped counter
    // accidentally match an ancient capture. No rollover allocation is needed.
    internal bool Matches(ulong revision) => revision != ulong.MaxValue && Revision == revision;

    internal void Invalidate(bool history = true)
    {
        _historyChanged |= history;
        if (Revision != ulong.MaxValue) Revision++;
    }

    internal void InvalidateStructure()
    {
        RequiresRebinding = true;
        Invalidate();
    }

    // Only the latest successful capture can use accumulated active-only
    // invalidations. Older/independent consumers fall back to exact comparison.
    internal int UnchangedHistoryPrefix(ulong capturedRevision) =>
        Revision != ulong.MaxValue && capturedRevision == _capturedRevision &&
        !_historyChanged && !RequiresRebinding ? ActiveStart : 0;

    internal void RecordCaptured()
    {
        _capturedRevision = Revision;
        _historyChanged = false;
    }
}
