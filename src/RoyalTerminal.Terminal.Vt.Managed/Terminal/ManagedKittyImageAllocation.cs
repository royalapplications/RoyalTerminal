// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

// Per-loader allocation checkpoints, kept internal for deterministic failure tests.
internal enum ManagedKittyImageAllocation
{
    Loader,
    BufferCopy,
    BufferGrowth,
    BufferTransfer,
    InflateStream,
    InflateScratch,
    Pixels,
}
