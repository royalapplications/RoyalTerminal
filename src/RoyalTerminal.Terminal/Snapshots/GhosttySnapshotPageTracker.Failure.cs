// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.ExceptionServices;

namespace RoyalTerminal.Terminal.Snapshots;

internal sealed partial class GhosttySnapshotPageTracker
{
    // Native clonePartialRowGrowCapacity panics after exhausted retries: its
    // caller has already shifted rows/anchors. The embedded managed engine
    // faults this terminal owner instead of terminating the host process.
    // The failure is owner-local, including across COW publication/rollback.
    private ExceptionDispatchInfo? _mutationFailure;

    internal bool MutationFailed => _mutationFailure is not null;
    internal void ThrowIfMutationFailed() => _mutationFailure?.Throw();

    internal void RecordMutationFailure(Exception failure)
        => _mutationFailure ??= ExceptionDispatchInfo.Capture(failure);

    private static InvalidOperationException RowCopyFailed()
        => new("Terminal row metadata copy exhausted page capacity. " +
            "The partially mutated terminal cannot be reused; create a new processor and screen.");
}
