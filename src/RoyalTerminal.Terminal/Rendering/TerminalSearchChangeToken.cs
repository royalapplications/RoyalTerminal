// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Lazy search invalidation shared by a live row buffer and its rows. Contains
/// no owner references, so retaining a capture cannot root a live screen/history.
/// Reads and writes require the owning screen lock, like row mutations themselves.
/// </summary>
internal sealed class TerminalSearchChangeToken(ulong revision = 0)
{
    internal ulong Revision { get; private set; } = revision;

    // Saturation permanently disables reuse instead of letting a wrapped counter
    // accidentally match an ancient capture. No rollover allocation is needed.
    internal bool Matches(ulong revision) => revision != ulong.MaxValue && Revision == revision;

    internal void Invalidate()
    {
        if (Revision != ulong.MaxValue) Revision++;
    }
}
