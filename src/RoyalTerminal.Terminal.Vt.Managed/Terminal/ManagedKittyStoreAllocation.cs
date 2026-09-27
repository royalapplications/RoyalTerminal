// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

// Per-operation checkpoints; observers never participate in committed mutations.
internal enum ManagedKittyStoreAllocation
{
    ImageCapacity,
    Image,
    PlacementCapacity,
    Anchor,
    Placement,
    ScrollRestoreCapacity,
}
