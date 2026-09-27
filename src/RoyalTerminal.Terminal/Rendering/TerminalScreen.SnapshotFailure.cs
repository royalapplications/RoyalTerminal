// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.ExceptionServices;

namespace RoyalTerminal.Avalonia.Rendering;

internal enum SnapshotMutationCheckpoint
{
    MetadataClear,
    MetadataWrite,
    GraphemeAppend,
    Revision,
    RowRetirement,
    HistoryPrepared,
    InactiveBufferPrepared,
    BufferSwitchPrepared,
    BufferSwitchPublished,
    RowRecycling,
    ThemeRowResolved,
    ResetPrepared,
    CursorResizeRestore,
    MetadataReconciliation,
    KittyImagesPrepared,
    KittyPlacementsPrepared,
    KittyScenePrepared,
    KittyProjectionPrepared,
    RasterPublicationPrepared,
}

public sealed partial class TerminalScreen
{
    private Exception? _snapshotMutationFailure;

    // Instance-local tripwire for deterministic partial-allocation tests. The
    // callback is copied into staging, never stored in global allocator state.
    internal Action<SnapshotMutationCheckpoint>? MutationCheckpoint { get; set; }

    internal bool SnapshotMutationFailed => _snapshotMutationFailure is not null || _snapshotPageTracker?.MutationFailed == true;

    internal void ThrowIfSnapshotMutationFailed()
    {
        if (_snapshotMutationFailure is { } failure) ExceptionDispatchInfo.Throw(failure);
        _snapshotPageTracker?.ThrowIfMutationFailed();
    }

    // Use in exception FILTERS, before RowEdit and cursor movement scopes
    // unwind. Neither recording a failure nor marking an untracked owner may
    // allocate, otherwise a second OOM can hide the partially mutated state.
    internal bool RecordSnapshotMutationFailure(Exception failure)
    {
        _snapshotMutationFailure ??= _snapshotPageTracker?.MutationFailure ?? failure;
        _snapshotPageTracker?.RecordMutationFailure(_snapshotMutationFailure);
        return true;
    }
}
