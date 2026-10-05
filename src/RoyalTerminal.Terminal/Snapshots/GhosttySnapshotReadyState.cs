// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Snapshots;

/// <summary>Owned, validated wire state through READY; not yet installed in a managed terminal.</summary>
internal sealed class GhosttySnapshotReadyState(
    GhosttySnapshotTerminalState terminal, GhosttySnapshotScreen[] screens, byte[] continuation)
{
    internal GhosttySnapshotTerminalState Terminal { get; } = terminal;
    internal ReadOnlySpan<GhosttySnapshotScreen> Screens => screens;
    internal ReadOnlySpan<byte> Continuation => continuation;
}

/// <summary>One screen's complete oldest-to-newest resident pages, including incidental history.</summary>
internal sealed class GhosttySnapshotScreen(GhosttySnapshotScreenState state, GhosttySnapshotPage[] pages)
{
    internal GhosttySnapshotScreenState State { get; } = state;
    internal ReadOnlySpan<GhosttySnapshotPage> Pages => pages;
}

/// <summary>One newest-to-oldest history page; installation/reconciliation is the adapter's responsibility.</summary>
internal readonly record struct GhosttySnapshotHistoryPage(int Key, GhosttySnapshotPage Page, uint Remaining);
