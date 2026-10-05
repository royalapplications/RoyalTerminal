// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

internal enum ManagedPublicationCheckpoint
{
    HoldPrepared,
    HoldPublishing,
}

public sealed partial class BasicVtProcessor
{
    // Instance-local, pre-commit tripwires. Unlike a partial metadata mutation,
    // failure here leaves the old owner/hold intact and permits a later retry.
    internal Action<ManagedPublicationCheckpoint>? PublicationCheckpoint { get; set; }
}
