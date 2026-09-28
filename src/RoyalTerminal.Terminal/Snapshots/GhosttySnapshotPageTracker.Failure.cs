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
    private Exception? _mutationFailure;

    internal bool MutationFailed => _mutationFailure is not null;
    internal Exception? MutationFailure => _mutationFailure;
    internal void ThrowIfMutationFailed()
    {
        if (_mutationFailure is { } failure) ExceptionDispatchInfo.Throw(failure);
    }

    internal void RecordMutationFailure(Exception failure)
        // Latching a CLR allocation failure must not itself allocate an EDI.
        => _mutationFailure ??= failure;

    private static InvalidOperationException RowCopyFailed()
        => new("Terminal row metadata copy exhausted page capacity. " +
            "The partially mutated terminal cannot be reused; create a new processor and screen.");
}
